namespace JobHunter.Domain;

/// <summary>The single settings row (Id 1): contact details used for prefill, resume paths, compensation bounds, the candidate's location, languages, currency, stack and contract preference, models, sources and run limits.</summary>
public sealed class Settings
{
    /// <summary>Identifier of the one and only settings row.</summary>
    public const int SingletonId = 1;

    /// <summary>The currency a new install counts pay in.</summary>
    public const string DefaultBaseCurrency = "EUR";

    /// <summary>The posting languages a new install accepts.</summary>
    public const string DefaultAcceptedLanguages = "en";

    private static readonly string[] DefaultTitleIncludeTermList =
    [
        ".net", "dotnet", "c#", "csharp",
        "engineer", "engineers", "engineering", "developer", "development", "programmer",
        "backend", "back end", "back-end", "server side", "server-side", "serverside",
        "platform", "infrastructure", "infra", "devops", "sre", "site reliability", "architect", "distributed", "system", "systems",
        "software", "cloud", "kubernetes", "api", "microservice", "microservices",
        "golang", "java", "python", "node", "node.js", "rust", "scala", "elixir",
        "database", "compiler", "tooling", "developer tools", "developer experience", "devex",
        "llm", "agent", "agents", "agentic", "ai"
    ];

    private static readonly string[] DefaultTitleExcludeTermList =
    [
        "front end", "front-end", "frontend", "ui engineer", "web designer", "react developer",
        "mobile", "android", "ios", "flutter", "react native",
        "qa", "quality assurance", "sdet", "tester", "test engineer", "test automation", "automation test",
        "sales", "account executive", "account manager", "business development", "pre-sales", "presales", "pre sales", "bdr", "sdr",
        "marketing", "seo", "copywriter", "content writer", "content marketer", "community manager",
        "designer", "ux", "ui/ux", "product design", "graphic design", "motion design",
        "data science", "data scientist", "data scientists", "data analyst", "analytics engineer", "business intelligence", "bi developer", "statistician",
        "machine learning research", "machine learning researcher", "ml research", "ml researcher", "ai research", "ai researcher", "deep learning research", "deep learning researcher", "research scientist", "applied scientist",
        "intern", "internship", "junior", "jr", "graduate", "entry level", "entry-level", "trainee", "apprentice", "working student", "student",
        "manager", "head of", "director", "vp", "vice president", "cto", "chief"
    ];

    /// <summary>The title terms that keep a job, one per line: the terms the title rules have always included.</summary>
    public static readonly string DefaultTitleIncludeTerms = string.Join('\n', DefaultTitleIncludeTermList);

    /// <summary>The title terms that drop a job, one per line: the terms the title rules have always excluded.</summary>
    public static readonly string DefaultTitleExcludeTerms = string.Join('\n', DefaultTitleExcludeTermList);

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

    public decimal? MinContractorHourly { get; private set; }

    public decimal? MinEmploymentAnnual { get; private set; }

    public decimal? TargetAnnual { get; private set; }

    /// <summary>ISO 3166-1 alpha-2 code of the country the candidate lives in; null when not set.</summary>
    public string? HomeCountryIso { get; private set; }

    public bool AcceptEuropeRemote { get; private set; }

    public bool AcceptUnitedStatesRemote { get; private set; }

    /// <summary>Comma-separated ISO 639-1 codes of the posting languages the candidate accepts.</summary>
    public string AcceptedLanguages { get; private set; } = string.Empty;

    /// <summary>ISO 4217 code of the currency pay is counted in: the comp minimums, the target and the high-pay threshold.</summary>
    public string BaseCurrency { get; private set; } = string.Empty;

    /// <summary>ISO 4217 code of the currency the stored job comp was last computed in; differs from <see cref="BaseCurrency"/> until the comp is recomputed.</summary>
    public string CompComputedInCurrency { get; private set; } = string.Empty;

    /// <summary>Comma-separated keywords of the candidate's stack, which drive the stack-match flag.</summary>
    public string StackKeywords { get; private set; } = string.Empty;

    public ContractPreference ContractPreference { get; private set; }

    public bool HasUnitedStatesWorkAuthorization { get; private set; }

    /// <summary>Pay per year in the base currency at or above which a job counts as high pay; null when not set.</summary>
    public decimal? HighPayThresholdPerYear { get; private set; }

    /// <summary>Title terms that keep a job, one per line.</summary>
    public string TitleIncludeTerms { get; private set; } = string.Empty;

    /// <summary>Title terms that drop a job, one per line.</summary>
    public string TitleExcludeTerms { get; private set; } = string.Empty;

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

    public bool KeepOnsiteWithCompOrRelocation { get; private set; }

    public string? FxOverridesJson { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates the settings row with neutral defaults: no home country, English postings, EUR, no stack keywords, either contract form, remote in Europe and the United States accepted; personal details stay empty until they are entered in the app.</summary>
    public static Settings CreateDefault()
    {
        return new Settings
        {
            Id = SingletonId,
            ResumePdfPath = "/home/app/resume/Resume.pdf",
            ResumeMarkdownPath = "/home/app/resume/Resume.md",
            HomeCountryIso = null,
            AcceptEuropeRemote = true,
            AcceptUnitedStatesRemote = true,
            AcceptedLanguages = DefaultAcceptedLanguages,
            BaseCurrency = DefaultBaseCurrency,
            CompComputedInCurrency = DefaultBaseCurrency,
            StackKeywords = string.Empty,
            ContractPreference = ContractPreference.Either,
            HasUnitedStatesWorkAuthorization = false,
            HighPayThresholdPerYear = null,
            TitleIncludeTerms = DefaultTitleIncludeTerms,
            TitleExcludeTerms = DefaultTitleExcludeTerms,
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
    public void ConfigureCompensation(decimal? minContractorHourly, decimal? minEmploymentAnnual, decimal? targetAnnual)
    {
        MinContractorHourly = minContractorHourly;
        MinEmploymentAnnual = minEmploymentAnnual;
        TargetAnnual = targetAnnual;
        Touch();
    }

    /// <summary>Records who the candidate is for the rules: home country, accepted remote regions and posting languages, base currency, stack keywords, contract preference, United States work authorization, the high-pay threshold and the title terms.</summary>
    public void ConfigureCandidate(
        string? homeCountryIso,
        bool acceptEuropeRemote,
        bool acceptUnitedStatesRemote,
        string acceptedLanguages,
        string baseCurrency,
        string stackKeywords,
        ContractPreference contractPreference,
        bool hasUnitedStatesWorkAuthorization,
        decimal? highPayThresholdPerYear,
        string titleIncludeTerms,
        string titleExcludeTerms)
    {
        ArgumentNullException.ThrowIfNull(acceptedLanguages);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);
        ArgumentNullException.ThrowIfNull(stackKeywords);
        ArgumentNullException.ThrowIfNull(titleIncludeTerms);
        ArgumentNullException.ThrowIfNull(titleExcludeTerms);
        ArgumentOutOfRangeException.ThrowIfNegative(highPayThresholdPerYear ?? 0m, nameof(highPayThresholdPerYear));

        HomeCountryIso = string.IsNullOrWhiteSpace(homeCountryIso) ? null : homeCountryIso.Trim().ToUpperInvariant();
        AcceptEuropeRemote = acceptEuropeRemote;
        AcceptUnitedStatesRemote = acceptUnitedStatesRemote;
        AcceptedLanguages = string.Join(',', acceptedLanguages.ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        BaseCurrency = baseCurrency.Trim().ToUpperInvariant();
        StackKeywords = stackKeywords.Trim();
        ContractPreference = contractPreference;
        HasUnitedStatesWorkAuthorization = hasUnitedStatesWorkAuthorization;
        HighPayThresholdPerYear = highPayThresholdPerYear;
        TitleIncludeTerms = OneTermPerLine(titleIncludeTerms);
        TitleExcludeTerms = OneTermPerLine(titleExcludeTerms);
        Touch();
    }

    /// <summary>Records the compensation bounds and the high-pay threshold as converted into the base currency, and marks the stored job comp as computed in it.</summary>
    public void RecordCompRecomputedInBaseCurrency(decimal? minContractorHourly, decimal? minEmploymentAnnual, decimal? targetAnnual, decimal? highPayThresholdPerYear)
    {
        MinContractorHourly = minContractorHourly;
        MinEmploymentAnnual = minEmploymentAnnual;
        TargetAnnual = targetAnnual;
        HighPayThresholdPerYear = highPayThresholdPerYear;
        CompComputedInCurrency = BaseCurrency;
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

    /// <summary>Records whether the geography rules keep onsite roles that post compensation or offer relocation.</summary>
    public void ConfigureGeographyRules(bool keepOnsiteWithCompOrRelocation)
    {
        KeepOnsiteWithCompOrRelocation = keepOnsiteWithCompOrRelocation;
        Touch();
    }

    /// <summary>Records manual exchange-rate overrides, which win over the daily rates fetched from the central bank.</summary>
    public void ConfigureFxOverrides(string? fxOverridesJson)
    {
        FxOverridesJson = string.IsNullOrWhiteSpace(fxOverridesJson) ? null : fxOverridesJson;
        Touch();
    }

    /// <summary>Trims every term, drops blank lines and joins what is left with a line feed, whatever line endings the text arrived with.</summary>
    private static string OneTermPerLine(string terms)
    {
        return string.Join('\n', terms.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
