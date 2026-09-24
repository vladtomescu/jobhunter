namespace JobHunter.Domain;

/// <summary>The single settings row (Id 1): contact details used for prefill, resume paths, compensation bounds, models, sources and run limits.</summary>
public sealed class Settings
{
    /// <summary>Identifier of the one and only settings row.</summary>
    public const int SingletonId = 1;

    private Settings()
    {
    }

    public int Id { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public string Location { get; private set; } = string.Empty;

    public string LinkedInUrl { get; private set; } = string.Empty;

    public string ResumePdfPath { get; private set; } = string.Empty;

    public string ResumeMarkdownPath { get; private set; } = string.Empty;

    public decimal? MinB2bHourlyEur { get; private set; }

    public decimal? MinEmploymentAnnualEur { get; private set; }

    public decimal? TargetAnnualEur { get; private set; }

    public string ScoreModel { get; private set; } = string.Empty;

    public string KitModel { get; private set; } = string.Empty;

    public bool RemoteOkEnabled { get; private set; }

    public bool WwrEnabled { get; private set; }

    public bool DatasetEnabled { get; private set; }

    public string DatasetAtsList { get; private set; } = string.Empty;

    public int FirstRunWindowDays { get; private set; }

    public int GhostThresholdDays { get; private set; }

    public int AutoRefreshAfterHours { get; private set; }

    public int MaxScoresPerRun { get; private set; }

    public bool KeepUsOnlyRemote { get; private set; }

    public bool KeepOnsiteWithCompOrRelocation { get; private set; }

    public string? FxOverridesJson { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates the settings row with the defaults from the requirements; personal contact details stay empty until they are entered in the app.</summary>
    public static Settings CreateDefault()
    {
        return new Settings
        {
            Id = SingletonId,
            ResumePdfPath = @"<resume-folder>\Resume.pdf",
            ResumeMarkdownPath = @"<resume-folder>\Resume.md",
            ScoreModel = "claude-opus-5",
            KitModel = "claude-opus-5",
            RemoteOkEnabled = true,
            WwrEnabled = true,
            DatasetEnabled = true,
            DatasetAtsList = "greenhouse,lever,ashby,workable",
            FirstRunWindowDays = 21,
            GhostThresholdDays = 21,
            AutoRefreshAfterHours = 12,
            MaxScoresPerRun = 300,
            KeepUsOnlyRemote = true,
            KeepOnsiteWithCompOrRelocation = true,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>Records the contact details that prefill types into application forms.</summary>
    public void ConfigureContact(string firstName, string lastName, string email, string phone, string location, string linkedInUrl)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Phone = phone;
        Location = location;
        LinkedInUrl = linkedInUrl;
        Touch();
    }

    /// <summary>Records where the resume lives: the PDF that is uploaded and the markdown that kits are written from.</summary>
    public void ConfigureResume(string resumePdfPath, string resumeMarkdownPath)
    {
        ResumePdfPath = resumePdfPath;
        ResumeMarkdownPath = resumeMarkdownPath;
        Touch();
    }

    /// <summary>Records the compensation bounds that scoring uses; kits never see these numbers.</summary>
    public void ConfigureCompensation(decimal? minB2bHourlyEur, decimal? minEmploymentAnnualEur, decimal? targetAnnualEur)
    {
        MinB2bHourlyEur = minB2bHourlyEur;
        MinEmploymentAnnualEur = minEmploymentAnnualEur;
        TargetAnnualEur = targetAnnualEur;
        Touch();
    }

    /// <summary>Records which model scores jobs and which one writes kits.</summary>
    public void ConfigureModels(string scoreModel, string kitModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scoreModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(kitModel);

        ScoreModel = scoreModel;
        KitModel = kitModel;
        Touch();
    }

    /// <summary>Records which sources a refresh reads and which applicant tracking systems the dataset slice covers.</summary>
    public void ConfigureSources(bool remoteOkEnabled, bool wwrEnabled, bool datasetEnabled, string datasetAtsList)
    {
        RemoteOkEnabled = remoteOkEnabled;
        WwrEnabled = wwrEnabled;
        DatasetEnabled = datasetEnabled;
        DatasetAtsList = datasetAtsList;
        Touch();
    }

    /// <summary>Records the run limits: intake window, ghost threshold, automatic refresh interval (zero turns the startup refresh off) and the per-run scoring cap.</summary>
    public void ConfigureRunLimits(int firstRunWindowDays, int ghostThresholdDays, int autoRefreshAfterHours, int maxScoresPerRun)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(firstRunWindowDays);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ghostThresholdDays);
        ArgumentOutOfRangeException.ThrowIfNegative(autoRefreshAfterHours);
        ArgumentOutOfRangeException.ThrowIfNegative(maxScoresPerRun);

        FirstRunWindowDays = firstRunWindowDays;
        GhostThresholdDays = ghostThresholdDays;
        AutoRefreshAfterHours = autoRefreshAfterHours;
        MaxScoresPerRun = maxScoresPerRun;
        Touch();
    }

    /// <summary>Records how wide the geography rules keep the net: United States only remote roles, and onsite roles that post compensation or offer relocation.</summary>
    public void ConfigureGeographyRules(bool keepUsOnlyRemote, bool keepOnsiteWithCompOrRelocation)
    {
        KeepUsOnlyRemote = keepUsOnlyRemote;
        KeepOnsiteWithCompOrRelocation = keepOnsiteWithCompOrRelocation;
        Touch();
    }

    /// <summary>Records manual exchange-rate overrides, which win over the daily rates fetched from the central bank.</summary>
    public void ConfigureFxOverrides(string? fxOverridesJson)
    {
        FxOverridesJson = string.IsNullOrWhiteSpace(fxOverridesJson) ? null : fxOverridesJson;
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
