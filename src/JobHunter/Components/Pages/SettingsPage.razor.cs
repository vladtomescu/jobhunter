using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using JobHunter.Data;
using JobHunter.Llm;
using JobHunter.Pipeline;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Pages;

/// <summary>Editable copy of the settings record bound to the form; saved back through the aggregate's business methods, never a public setter.</summary>
internal sealed class SettingsFormModel
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string LinkedInUrl { get; set; } = string.Empty;

    public string ResumePdfPath { get; set; } = string.Empty;

    public string ResumeMarkdownPath { get; set; } = string.Empty;

    public decimal? MinB2bHourlyEur { get; set; }

    public decimal? MinEmploymentAnnualEur { get; set; }

    public decimal? TargetAnnualEur { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "The score model cannot be blank.")]
    public string ScoreModel { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "The kit model cannot be blank.")]
    public string KitModel { get; set; } = string.Empty;

    public bool RemoteOkEnabled { get; set; }

    public bool WwrEnabled { get; set; }

    public bool DatasetEnabled { get; set; }

    public string DatasetAtsList { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "The first-run window must be at least 1 day.")]
    public int FirstRunWindowDays { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "The ghost threshold must be at least 1 day.")]
    public int GhostThresholdDays { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "The auto-refresh interval cannot be negative; 0 turns the startup refresh off.")]
    public int AutoRefreshAfterHours { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "The per-run scoring cap cannot be negative.")]
    public int MaxScoresPerRun { get; set; }

    public bool KeepUsOnlyRemote { get; set; }

    public bool KeepOnsiteWithCompOrRelocation { get; set; }

    public string? FxOverridesJson { get; set; }
}

/// <summary>Settings page: every settings field, the detected API key state, resume path checks and an on-demand FX rate fetch. Named `SettingsPage` because the plain name `Settings` collides with the `JobHunter.Settings` namespace and the `JobHunter.Domain.Settings` aggregate.</summary>
public sealed partial class SettingsPage : IDisposable
{
    [Inject]
    private JobHunter.Settings.SettingsService SettingsService { get; set; } = null!;

    [Inject]
    private ApiKeyDetector ApiKeyDetector { get; set; } = null!;

    [Inject]
    private IFxRateProvider FxRateProvider { get; set; } = null!;

    [Inject]
    private DataPaths DataPaths { get; set; } = null!;

    private SettingsFormModel Model { get; set; } = new();

    private JobHunter.Domain.Settings? currentSettings;

    private bool isSaving;

    private bool savedConfirmationVisible;

    private string? fxOverridesError;

    private bool isFetchingFxRate;

    private string? fxFetchResult;

    private bool fxFetchFailed;

    private readonly CancellationTokenSource componentLifetime = new();

    /// <summary>The B2B hourly rate as most recently typed, tracked on every keystroke through <see cref="OnB2bHourlyRateInput"/> so the equivalent below the field is live; the field bound to the form model itself still only commits on change, so a parse failure here never touches it.</summary>
    private decimal? liveB2bHourlyEur;

    private bool ResumePdfExists => Model.ResumePdfPath.Length > 0 && File.Exists(Model.ResumePdfPath);

    private bool ResumeMarkdownExists => Model.ResumeMarkdownPath.Length > 0 && File.Exists(Model.ResumeMarkdownPath);

    /// <summary>The annualization CompNormalizer applies to an hourly B2B rate, formatted for the help text below the field.</summary>
    private static string HoursPerYearText => CompNormalizer.HoursPerYear.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The working days CompNormalizer implies for that same annualization, formatted for the help text below the field.</summary>
    private static string DaysPerYearText => CompNormalizer.DaysPerYear.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The hours-per-month CompNormalizer's constants imply (hours/year ÷ months/year), formatted for the help text below the field.</summary>
    private static string HoursPerMonthText => (CompNormalizer.HoursPerYear / CompNormalizer.MonthsPerYear).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>Renders the live monthly and yearly equivalent of a typed B2B hourly rate, using CompNormalizer's own annualization constants.</summary>
    private static string FormatB2bEquivalent(decimal hourlyEurPerHour)
    {
        decimal yearlyEur = decimal.Round(hourlyEurPerHour * CompNormalizer.HoursPerYear, 0, MidpointRounding.AwayFromZero);
        decimal monthlyEur = decimal.Round(yearlyEur / CompNormalizer.MonthsPerYear, 0, MidpointRounding.AwayFromZero);

        return $"{hourlyEurPerHour.ToString("0.##", CultureInfo.InvariantCulture)} EUR/h ≈ {monthlyEur.ToString("#,##0", CultureInfo.InvariantCulture)} EUR/month ≈ {yearlyEur.ToString("#,##0", CultureInfo.InvariantCulture)} EUR/year";
    }

    /// <summary>Tracks the B2B hourly rate on every keystroke for the live equivalent below the field, without going through the form model's own change-only binding.</summary>
    private void OnB2bHourlyRateInput(ChangeEventArgs args)
    {
        liveB2bHourlyEur = decimal.TryParse(args.Value?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : null;
    }

    private DateTimeOffset? FxCacheLastWrittenAtLocal
    {
        get
        {
            string cacheFile = Path.Combine(DataPaths.Fx, EcbFxRateProvider.CacheFileName);

            return File.Exists(cacheFile) ? new DateTimeOffset(File.GetLastWriteTimeUtc(cacheFile), TimeSpan.Zero).ToLocalTime() : null;
        }
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        currentSettings = await SettingsService.GetAsync();
        Model = ToFormModel(currentSettings);
        liveB2bHourlyEur = Model.MinB2bHourlyEur;
    }

    private static SettingsFormModel ToFormModel(JobHunter.Domain.Settings settings)
    {
        return new SettingsFormModel
        {
            FirstName = settings.FirstName,
            LastName = settings.LastName,
            Email = settings.Email,
            Phone = settings.Phone,
            Location = settings.Location,
            LinkedInUrl = settings.LinkedInUrl,
            ResumePdfPath = settings.ResumePdfPath,
            ResumeMarkdownPath = settings.ResumeMarkdownPath,
            MinB2bHourlyEur = settings.MinB2bHourlyEur,
            MinEmploymentAnnualEur = settings.MinEmploymentAnnualEur,
            TargetAnnualEur = settings.TargetAnnualEur,
            ScoreModel = settings.ScoreModel,
            KitModel = settings.KitModel,
            RemoteOkEnabled = settings.RemoteOkEnabled,
            WwrEnabled = settings.WwrEnabled,
            DatasetEnabled = settings.DatasetEnabled,
            DatasetAtsList = settings.DatasetAtsList,
            FirstRunWindowDays = settings.FirstRunWindowDays,
            GhostThresholdDays = settings.GhostThresholdDays,
            AutoRefreshAfterHours = settings.AutoRefreshAfterHours,
            MaxScoresPerRun = settings.MaxScoresPerRun,
            KeepUsOnlyRemote = settings.KeepUsOnlyRemote,
            KeepOnsiteWithCompOrRelocation = settings.KeepOnsiteWithCompOrRelocation,
            FxOverridesJson = settings.FxOverridesJson
        };
    }

    private async Task SaveAsync()
    {
        savedConfirmationVisible = false;
        fxOverridesError = null;

        if (!TryValidateFxOverrides(out string? validatedFxOverrides))
        {
            return;
        }

        isSaving = true;
        try
        {
            currentSettings = await SettingsService.ApplyAsync(settings =>
            {
                settings.ConfigureContact(Model.FirstName.Trim(), Model.LastName.Trim(), Model.Email.Trim(), Model.Phone.Trim(), Model.Location.Trim(), Model.LinkedInUrl.Trim());
                settings.ConfigureResume(Model.ResumePdfPath.Trim(), Model.ResumeMarkdownPath.Trim());
                settings.ConfigureCompensation(Model.MinB2bHourlyEur, Model.MinEmploymentAnnualEur, Model.TargetAnnualEur);
                settings.ConfigureModels(Model.ScoreModel.Trim(), Model.KitModel.Trim());
                settings.ConfigureSources(Model.RemoteOkEnabled, Model.WwrEnabled, Model.DatasetEnabled, Model.DatasetAtsList.Trim());
                settings.ConfigureRunLimits(Model.FirstRunWindowDays, Model.GhostThresholdDays, Model.AutoRefreshAfterHours, Model.MaxScoresPerRun);
                settings.ConfigureGeographyRules(Model.KeepUsOnlyRemote, Model.KeepOnsiteWithCompOrRelocation);
                settings.ConfigureFxOverrides(validatedFxOverrides);
            });

            Model = ToFormModel(currentSettings);
            liveB2bHourlyEur = Model.MinB2bHourlyEur;
            savedConfirmationVisible = true;
        }
        finally
        {
            isSaving = false;
        }
    }

    private bool TryValidateFxOverrides(out string? validated)
    {
        string? fxOverridesJson = Model.FxOverridesJson;

        if (string.IsNullOrWhiteSpace(fxOverridesJson))
        {
            validated = null;
            return true;
        }

        try
        {
            Dictionary<string, decimal>? overrides = JsonSerializer.Deserialize<Dictionary<string, decimal>>(fxOverridesJson);
            if (overrides is null)
            {
                fxOverridesError = "FX overrides must be a JSON object, for example {\"USD\": 1.08}.";
                validated = null;
                return false;
            }

            validated = fxOverridesJson;
            return true;
        }
        catch (JsonException)
        {
            fxOverridesError = "FX overrides must be valid JSON, for example {\"USD\": 1.08}.";
            validated = null;
            return false;
        }
    }

    private async Task FetchFxRateNowAsync()
    {
        fxFetchResult = null;
        fxFetchFailed = false;
        isFetchingFxRate = true;

        try
        {
            JobHunter.Domain.Settings settingsForFetch = currentSettings ?? await SettingsService.GetAsync();
            decimal? rate = await FxRateProvider.GetUnitsPerEuroAsync("USD", settingsForFetch, componentLifetime.Token);

            fxFetchFailed = rate is null;
            fxFetchResult = rate is null
                ? "Fetch failed: no USD rate available."
                : $"1 EUR = {rate.Value.ToString("0.####", CultureInfo.InvariantCulture)} USD";
        }
        catch (OperationCanceledException)
        {
            fxFetchResult = null;
        }
        finally
        {
            isFetchingFxRate = false;
        }
    }

    /// <summary>Cancels an in-flight rate fetch when the user navigates away from the page.</summary>
    public void Dispose()
    {
        componentLifetime.Cancel();
        componentLifetime.Dispose();
    }
}
