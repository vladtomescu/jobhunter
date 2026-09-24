using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
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

    public decimal? MinContractorHourly { get; set; }

    public decimal? MinEmploymentAnnual { get; set; }

    public decimal? TargetAnnual { get; set; }

    [RegularExpression(@"^\s*([A-Za-z]{2})?\s*$", ErrorMessage = "The home country must be a two-letter ISO code, for example DE, or blank.")]
    public string HomeCountryIso { get; set; } = string.Empty;

    public bool AcceptEuropeRemote { get; set; }

    public bool AcceptUnitedStatesRemote { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Accept at least one posting language.")]
    [RegularExpression(@"^\s*[A-Za-z]{2}(\s*,\s*[A-Za-z]{2})*\s*$", ErrorMessage = "Posting languages must be two-letter ISO codes separated by commas, for example en, de.")]
    public string AcceptedLanguages { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "The base currency cannot be blank.")]
    [RegularExpression(@"^\s*[A-Za-z]{3}\s*$", ErrorMessage = "The base currency must be a three-letter ISO code, for example EUR.")]
    public string BaseCurrency { get; set; } = string.Empty;

    public string StackKeywords { get; set; } = string.Empty;

    public ContractPreference ContractPreference { get; set; }

    public bool HasUnitedStatesWorkAuthorization { get; set; }

    [Range(0d, double.MaxValue, ErrorMessage = "The high-pay threshold cannot be negative.")]
    public decimal? HighPayThresholdPerYear { get; set; }

    public string TitleIncludeTerms { get; set; } = string.Empty;

    public string TitleExcludeTerms { get; set; } = string.Empty;

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

    public bool KeepOnsiteWithCompOrRelocation { get; set; }

    public string? FxOverridesJson { get; set; }
}

/// <summary>Settings page: every settings field, the detected API key state, resume path checks, an on-demand FX rate fetch and the delete of old jobs. Named `SettingsPage` because the plain name `Settings` collides with the `JobHunter.Settings` namespace and the `JobHunter.Domain.Settings` aggregate.</summary>
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

    [Inject]
    private JobRetentionService JobRetentionService { get; set; } = null!;

    [Inject]
    private CompRecomputeService CompRecomputeService { get; set; } = null!;

    /// <summary>The age the delete of old jobs starts at when the page opens, raised to the first-run window when that is longer.</summary>
    private const int DefaultRetentionDays = 30;

    private SettingsFormModel Model { get; set; } = new();

    private JobHunter.Domain.Settings? currentSettings;

    private bool isSaving;

    private bool savedConfirmationVisible;

    private string? fxOverridesError;

    private bool isFetchingFxRate;

    private string? fxFetchResult;

    private bool fxFetchFailed;

    private readonly CancellationTokenSource componentLifetime = new();

    /// <summary>The age in days typed into the delete of old jobs; null while the field is empty.</summary>
    private int? retentionDays;

    /// <summary>How many jobs a delete at <see cref="retentionDays"/> would remove, as last counted.</summary>
    private int oldJobCount;

    private string? retentionRefusal;

    private bool isConfirmingDelete;

    private bool isDeletingOldJobs;

    private string? retentionResult;

    private bool isRecomputingComp;

    private string? compRecomputeMessage;

    private bool compRecomputeFailed;

    /// <summary>True while the saved base currency differs from the currency the stored comp was computed in, which is when the recompute is offered.</summary>
    private bool CompNeedsRecompute => currentSettings is not null && !string.Equals(currentSettings.BaseCurrency, currentSettings.CompComputedInCurrency, StringComparison.OrdinalIgnoreCase);

    /// <summary>The smallest age the delete accepts, the saved first-run window.</summary>
    private int RetentionMinimumDays => currentSettings?.FirstRunWindowDays ?? 0;

    /// <summary>The contractor hourly rate as most recently typed, tracked on every keystroke through <see cref="OnB2bHourlyRateInput"/> so the equivalent below the field is live; the field bound to the form model itself still only commits on change, so a parse failure here never touches it.</summary>
    private decimal? liveContractorHourly;

    private bool ResumePdfExists => Model.ResumePdfPath.Length > 0 && File.Exists(Model.ResumePdfPath);

    private bool ResumeMarkdownExists => Model.ResumeMarkdownPath.Length > 0 && File.Exists(Model.ResumeMarkdownPath);

    /// <summary>The annualization CompNormalizer applies to an hourly B2B rate, formatted for the help text below the field.</summary>
    private static string HoursPerYearText => CompNormalizer.HoursPerYear.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The working days CompNormalizer implies for that same annualization, formatted for the help text below the field.</summary>
    private static string DaysPerYearText => CompNormalizer.DaysPerYear.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The hours-per-month CompNormalizer's constants imply (hours/year ÷ months/year), formatted for the help text below the field.</summary>
    private static string HoursPerMonthText => (CompNormalizer.HoursPerYear / CompNormalizer.MonthsPerYear).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>The saved base currency, which the compensation fields are counted in; an unsaved edit of the currency field does not relabel them.</summary>
    private string SavedBaseCurrency => currentSettings?.BaseCurrency ?? JobHunter.Domain.Settings.DefaultBaseCurrency;

    /// <summary>The wording the contract preference choice shows for each value.</summary>
    private static string ContractPreferenceLabel(ContractPreference preference)
    {
        return preference switch
        {
            ContractPreference.Contractor => "Contractor (B2B)",
            ContractPreference.Employee => "Employee",
            _ => "Either"
        };
    }

    /// <summary>Renders the live monthly and yearly equivalent of a typed contractor hourly rate in the given currency, using CompNormalizer's own annualization constants.</summary>
    private static string FormatB2bEquivalent(decimal hourlyRate, string currency)
    {
        decimal yearly = decimal.Round(hourlyRate * CompNormalizer.HoursPerYear, 0, MidpointRounding.AwayFromZero);
        decimal monthly = decimal.Round(yearly / CompNormalizer.MonthsPerYear, 0, MidpointRounding.AwayFromZero);

        return $"{hourlyRate.ToString("0.##", CultureInfo.InvariantCulture)} {currency}/h ≈ {monthly.ToString("#,##0", CultureInfo.InvariantCulture)} {currency}/month ≈ {yearly.ToString("#,##0", CultureInfo.InvariantCulture)} {currency}/year";
    }

    /// <summary>Tracks the contractor hourly rate on every keystroke for the live equivalent below the field, without going through the form model's own change-only binding.</summary>
    private void OnB2bHourlyRateInput(ChangeEventArgs args)
    {
        liveContractorHourly = decimal.TryParse(args.Value?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : null;
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

        retentionDays = Math.Max(DefaultRetentionDays, RetentionMinimumDays);
        await CountOldJobsAsync();
    }

    private async Task LoadAsync()
    {
        currentSettings = await SettingsService.GetAsync();
        Model = ToFormModel(currentSettings);
        liveContractorHourly = Model.MinContractorHourly;
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
            MinContractorHourly = settings.MinContractorHourly,
            MinEmploymentAnnual = settings.MinEmploymentAnnual,
            TargetAnnual = settings.TargetAnnual,
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
            HomeCountryIso = settings.HomeCountryIso ?? string.Empty,
            AcceptEuropeRemote = settings.AcceptEuropeRemote,
            AcceptUnitedStatesRemote = settings.AcceptUnitedStatesRemote,
            AcceptedLanguages = settings.AcceptedLanguages,
            BaseCurrency = settings.BaseCurrency,
            StackKeywords = settings.StackKeywords,
            ContractPreference = settings.ContractPreference,
            HasUnitedStatesWorkAuthorization = settings.HasUnitedStatesWorkAuthorization,
            HighPayThresholdPerYear = settings.HighPayThresholdPerYear,
            TitleIncludeTerms = settings.TitleIncludeTerms,
            TitleExcludeTerms = settings.TitleExcludeTerms,
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
                settings.ConfigureCompensation(Model.MinContractorHourly, Model.MinEmploymentAnnual, Model.TargetAnnual);
                settings.ConfigureModels(Model.ScoreModel.Trim(), Model.KitModel.Trim());
                settings.ConfigureSources(Model.RemoteOkEnabled, Model.WwrEnabled, Model.DatasetEnabled, Model.DatasetAtsList.Trim());
                settings.ConfigureRunLimits(Model.FirstRunWindowDays, Model.GhostThresholdDays, Model.AutoRefreshAfterHours, Model.MaxScoresPerRun);
                settings.ConfigureCandidate(Model.HomeCountryIso, Model.AcceptEuropeRemote, Model.AcceptUnitedStatesRemote, Model.AcceptedLanguages, Model.BaseCurrency, Model.StackKeywords, Model.ContractPreference, Model.HasUnitedStatesWorkAuthorization, Model.HighPayThresholdPerYear, Model.TitleIncludeTerms, Model.TitleExcludeTerms);
                settings.ConfigureGeographyRules(Model.KeepOnsiteWithCompOrRelocation);
                settings.ConfigureFxOverrides(validatedFxOverrides);
            });

            Model = ToFormModel(currentSettings);
            liveContractorHourly = Model.MinContractorHourly;
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
            string baseCurrency = settingsForFetch.BaseCurrency;
            string quotedCurrency = string.Equals(baseCurrency, "USD", StringComparison.OrdinalIgnoreCase) ? EcbFxRateProvider.QuoteCurrency : "USD";
            decimal? rate = await FxRateProvider.GetUnitsPerBaseAsync(quotedCurrency, baseCurrency, settingsForFetch, componentLifetime.Token);

            fxFetchFailed = rate is null;
            fxFetchResult = rate is null
                ? $"Fetch failed: no {quotedCurrency} rate available."
                : $"1 {baseCurrency} = {rate.Value.ToString("0.####", CultureInfo.InvariantCulture)} {quotedCurrency}";
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

    /// <summary>Converts the stored pay and the compensation bounds into the saved base currency and reclassifies the scored jobs, then reloads the form so it shows the converted bounds.</summary>
    private async Task RecomputeCompAsync()
    {
        compRecomputeMessage = null;
        compRecomputeFailed = false;
        isRecomputingComp = true;

        try
        {
            CompRecomputeResult result = await CompRecomputeService.RecomputeAsync(componentLifetime.Token);

            if (result.Refusal is string refusal)
            {
                compRecomputeFailed = true;
                compRecomputeMessage = refusal;

                return;
            }

            await LoadAsync();
            savedConfirmationVisible = false;
            compRecomputeMessage = $"Recomputed in {currentSettings?.BaseCurrency}: pay converted on {JobCountText(result.JobsWithPay)}, {JobCountText(result.ClassesChanged)} changed class.";
        }
        catch (OperationCanceledException)
        {
            compRecomputeMessage = null;
        }
        finally
        {
            isRecomputingComp = false;
        }
    }

    /// <summary>Counts again after every keystroke in the days field, which also withdraws a pending confirmation and the previous result.</summary>
    private async Task PreviewOldJobsAsync()
    {
        isConfirmingDelete = false;
        retentionResult = null;

        await CountOldJobsAsync();
    }

    /// <summary>Counts the jobs a delete at the typed age would remove, or shows why that age is refused; a count overtaken by a newer keystroke is discarded.</summary>
    private async Task CountOldJobsAsync()
    {
        if (retentionDays is not int days)
        {
            retentionRefusal = "Enter a number of days.";
            oldJobCount = 0;

            return;
        }

        JobRetentionOutcome outcome = await JobRetentionService.CountJobsOlderThanAsync(days, componentLifetime.Token);

        if (retentionDays != days)
        {
            return;
        }

        retentionRefusal = outcome.Refusal;
        oldJobCount = outcome.Jobs;
    }

    private void AskToConfirmDelete()
    {
        retentionResult = null;
        isConfirmingDelete = oldJobCount > 0;
    }

    private void CancelDelete()
    {
        isConfirmingDelete = false;
    }

    private async Task DeleteOldJobsAsync()
    {
        if (retentionDays is not int days)
        {
            return;
        }

        isDeletingOldJobs = true;

        try
        {
            JobRetentionOutcome outcome = await JobRetentionService.DeleteJobsOlderThanAsync(days, componentLifetime.Token);
            isConfirmingDelete = false;

            await CountOldJobsAsync();

            if (outcome.Refusal is string deleteRefusal)
            {
                retentionRefusal = deleteRefusal;
            }
            else
            {
                retentionResult = $"Deleted {JobCountText(outcome.Jobs)}.";
            }
        }
        finally
        {
            isDeletingOldJobs = false;
        }
    }

    private static string JobCountText(int jobs)
    {
        return jobs == 1 ? "1 job" : $"{jobs} jobs";
    }

    /// <summary>Cancels an in-flight rate fetch when the user navigates away from the page.</summary>
    public void Dispose()
    {
        componentLifetime.Cancel();
        componentLifetime.Dispose();
    }
}
