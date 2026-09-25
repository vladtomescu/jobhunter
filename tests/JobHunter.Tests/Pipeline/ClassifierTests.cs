using JobHunter.Domain;
using JobHunter.Llm.Contracts;
using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that the class is decided in code: the compensation signal is recomputed from the settings and the rubric total is read against the class rules.</summary>
public sealed class ClassifierTests
{
    [Fact]
    public void Classify_WhenStatedCompReachesTheTarget_ScoresTheCompensationSignalFull()
    {
        Classification result = Classifier.Classify(Input(comp: new YearlyComp(120_000m, 140_000m), minB2bHourly: 45m, target: 120_000m));

        Assert.Equal(2, result.CompSignal);
    }

    [Fact]
    public void Classify_WhenCompIsNotStated_ScoresTheCompensationSignalNeutral()
    {
        Classification result = Classifier.Classify(Input(modelCompSignal: 2, comp: YearlyComp.Unknown, minB2bHourly: 45m, target: 120_000m));

        Assert.Equal(1, result.CompSignal);
    }

    [Fact]
    public void Classify_WhenStatedCompSitsBetweenTheMinimumAndTheTarget_ScoresTheCompensationSignalNeutral()
    {
        Classification result = Classifier.Classify(Input(modelCompSignal: 0, comp: new YearlyComp(95_000m, 105_000m), minB2bHourly: 45m, target: 120_000m));

        Assert.Equal(1, result.CompSignal);
    }

    [Fact]
    public void Classify_WhenStatedCompIsBelowTheMinimum_ScoresTheCompensationSignalZero()
    {
        Classification result = Classifier.Classify(Input(modelCompSignal: 2, comp: new YearlyComp(40_000m, 55_000m), minB2bHourly: 45m, target: 120_000m));

        Assert.Equal(0, result.CompSignal);
    }

    [Fact]
    public void Classify_WhenNoCompensationBoundIsConfigured_KeepsTheSignalTheModelGave()
    {
        Classification result = Classifier.Classify(Input(modelCompSignal: 2, comp: new YearlyComp(30_000m, 30_000m)));

        Assert.Equal(2, result.CompSignal);
    }

    [Fact]
    public void Classify_WhenStatedCompIsBelowTheMinimum_ReturnsClassCWhateverTheTotal()
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2,
            comp: new YearlyComp(40_000m, 55_000m),
            minB2bHourly: 45m));

        Assert.Equal(JobClass.C, result.Class);
    }

    [Fact]
    public void Classify_ForAnEmploymentRole_ReadsTheMinimumAgainstTheAnnualEmploymentBound()
    {
        Classification result = Classifier.Classify(Input(
            employmentType: "employment",
            comp: new YearlyComp(50_000m, 50_000m),
            minB2bHourly: 10m,
            minEmploymentAnnual: 65_000m));

        Assert.Equal(0, result.CompSignal);
        Assert.Equal(JobClass.C, result.Class);
    }

    [Fact]
    public void Classify_ForAContractRole_ReadsTheMinimumAgainstTheAnnualizedHourlyBound()
    {
        Classification result = Classifier.Classify(Input(
            employmentType: "b2b",
            comp: new YearlyComp(95_000m, 95_000m),
            minB2bHourly: 45m,
            minEmploymentAnnual: 200_000m));

        Assert.Equal(1, result.CompSignal);
    }

    [Fact]
    public void Classify_ForAStrongFitWithoutBlockingUnknowns_ReturnsClassA()
    {
        Classification result = Classifier.Classify(Input(niche: 2, level: 2, stack: 2, remoteTimezone: 1, contractForm: 1, companySignal: 1));

        Assert.Equal(10, result.Total);
        Assert.Equal(JobClass.A, result.Class);
    }

    [Fact]
    public void Classify_ForAStrongFitWithABlockingUnknown_ReturnsClassB()
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 1, contractForm: 1, companySignal: 1,
            blockingUnknowns: ["us_authorization"]));

        Assert.Equal(JobClass.B, result.Class);
    }

    [Fact]
    public void Classify_ForAStrongTotalOutsideTheNiche_ReturnsClassB()
    {
        Classification result = Classifier.Classify(Input(niche: 0, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2));

        Assert.Equal(JobClass.B, result.Class);
    }

    [Theory]
    [InlineData(false, JobClass.A)]
    [InlineData(true, JobClass.C)]
    public void Classify_ForAStrongFitAndTheUnitedStatesWorkAuthorizationFact_ReturnsTheClassTheCapNames(bool requiresUsAuthorization, JobClass expected)
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 1, contractForm: 1, companySignal: 1,
            requiresUsAuthorization: requiresUsAuthorization));

        Assert.Equal(10, result.Total);
        Assert.Equal(expected, result.Class);
    }

    [Fact]
    public void Classify_ForAWeakFitThatRequiresUnitedStatesWorkAuthorization_KeepsClassD()
    {
        Classification result = Classifier.Classify(Input(
            niche: 0, level: 0, stack: 1, remoteTimezone: 0, contractForm: 0, companySignal: 0,
            requiresUsAuthorization: true));

        Assert.Equal(JobClass.D, result.Class);
    }

    [Theory]
    [InlineData(0, 1, 0, 2, 1, 2)]
    [InlineData(0, 1, 1, 2, 1, 2)]
    public void Classify_ForABandTotalWithNicheAndStackBelowTwo_ReturnsClassC(int niche, int level, int stack, int remoteTimezone, int contractForm, int companySignal)
    {
        Classification result = Classifier.Classify(Input(niche: niche, level: level, stack: stack, remoteTimezone: remoteTimezone, contractForm: contractForm, companySignal: companySignal));

        Assert.InRange(result.Total, 7, 9);
        Assert.Equal(JobClass.C, result.Class);
    }

    [Fact]
    public void Classify_ForABandTotalWithNicheAndStackOfTwo_KeepsClassB()
    {
        Classification result = Classifier.Classify(Input(niche: 0, level: 1, stack: 2, remoteTimezone: 1, contractForm: 1, companySignal: 1));

        Assert.Equal(7, result.Total);
        Assert.Equal(JobClass.B, result.Class);
    }

    [Fact]
    public void Classify_ForAStrongTotalWithNicheTwoAndStackZero_ReturnsClassA()
    {
        Classification result = Classifier.Classify(Input(niche: 2, level: 2, stack: 0, remoteTimezone: 2, contractForm: 2, companySignal: 1));

        Assert.Equal(10, result.Total);
        Assert.Equal(JobClass.A, result.Class);
    }

    [Theory]
    [InlineData(0, false, JobClass.C)]
    [InlineData(0, null, JobClass.C)]
    [InlineData(0, true, JobClass.B)]
    [InlineData(1, false, JobClass.B)]
    public void Classify_ForABandTotalAndTheEndClientFact_ReturnsTheClassTheCapNames(int companySignal, bool? endClientNamed, JobClass expected)
    {
        Classification result = Classifier.Classify(Input(
            niche: 1, level: 1, stack: 2, remoteTimezone: 1, contractForm: 1, companySignal: companySignal,
            endClientNamed: endClientNamed));

        Assert.InRange(result.Total, 7, 9);
        Assert.Equal(expected, result.Class);
    }

    [Theory]
    [InlineData(1, 1, 2, 1, 1, 1, JobClass.B)]
    [InlineData(1, 1, 1, 0, 1, 0, JobClass.C)]
    [InlineData(0, 0, 1, 0, 0, 0, JobClass.D)]
    public void Classify_ForARubricTotal_ReturnsTheClassTheRulesName(int niche, int level, int stack, int remoteTimezone, int contractForm, int companySignal, JobClass expected)
    {
        Classification result = Classifier.Classify(Input(niche: niche, level: level, stack: stack, remoteTimezone: remoteTimezone, contractForm: contractForm, companySignal: companySignal));

        Assert.Equal(expected, result.Class);
    }

    [Fact]
    public void Classify_ForAJobThePrefilterDropped_ReturnsClassD()
    {
        Classification result = Classifier.Classify(Input(niche: 2, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2, prefilterDropped: true));

        Assert.Equal(JobClass.D, result.Class);
    }

    [Fact]
    public void Classify_ForAScoredJob_ReturnsTheTotalOfTheSevenDimensions()
    {
        Classification result = Classifier.Classify(Input(niche: 2, level: 1, stack: 2, remoteTimezone: 1, contractForm: 2, companySignal: 1, modelCompSignal: 0));

        Assert.Equal(9, result.Total);
    }

    [Theory]
    [InlineData("employment", 65_000)]
    [InlineData("b2b", 79_200)]
    [InlineData("either", 79_200)]
    [InlineData("unknown", 79_200)]
    public void MinimumPerYear_ForAContractorAndAnEmploymentType_ReadsTheBoundThatApplies(string employmentType, int expected)
    {
        JobHunter.Domain.Settings settings = NewSettings(ContractPreference.Contractor, 45m, 65_000m, 120_000m);

        Assert.Equal((decimal)expected, Classifier.MinimumPerYear(employmentType, settings));
    }

    [Theory]
    [InlineData(ContractPreference.Contractor, "unknown", 79_200)]
    [InlineData(ContractPreference.Contractor, "either", 79_200)]
    [InlineData(ContractPreference.Employee, "unknown", 65_000)]
    [InlineData(ContractPreference.Employee, "either", 65_000)]
    [InlineData(ContractPreference.Either, "unknown", 65_000)]
    [InlineData(ContractPreference.Either, "either", 65_000)]
    [InlineData(ContractPreference.Employee, "b2b", 79_200)]
    [InlineData(ContractPreference.Either, "b2b", 79_200)]
    [InlineData(ContractPreference.Contractor, "employment", 65_000)]
    [InlineData(ContractPreference.Either, "employment", 65_000)]
    public void MinimumPerYear_ForAContractPreference_ReadsTheBoundThePostingOrThePreferenceNames(ContractPreference preference, string employmentType, int expected)
    {
        JobHunter.Domain.Settings settings = NewSettings(preference, 45m, 65_000m, null);

        Assert.Equal((decimal)expected, Classifier.MinimumPerYear(employmentType, settings));
    }

    [Theory]
    [InlineData(40, 95_000, 70_400)]
    [InlineData(null, 95_000, 95_000)]
    [InlineData(40, null, 70_400)]
    public void MinimumPerYear_ForEitherFormAndAnUnstatedType_ReadsTheLowerBoundOrTheOneThatIsSet(int? contractorHourly, int? employmentAnnual, int expected)
    {
        JobHunter.Domain.Settings settings = NewSettings(ContractPreference.Either, contractorHourly, employmentAnnual, null);

        Assert.Equal((decimal)expected, Classifier.MinimumPerYear("unknown", settings));
    }

    [Fact]
    public void MinimumPerYear_WhenNoBoundIsConfigured_IsUnknown()
    {
        Assert.Null(Classifier.MinimumPerYear("b2b", JobHunter.Domain.Settings.CreateDefault()));
    }

    [Theory]
    [InlineData(ContractPreference.Contractor, JobClass.B)]
    [InlineData(ContractPreference.Employee, JobClass.A)]
    [InlineData(ContractPreference.Either, JobClass.A)]
    public void Classify_ForAStrongFitWhoseOnlyOpenQuestionIsAContract_BlocksClassAOnlyForAContractor(ContractPreference preference, JobClass expected)
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 1, contractForm: 1, companySignal: 1,
            blockingUnknowns: ["b2b"],
            contractPreference: preference));

        Assert.Equal(expected, result.Class);
    }

    [Theory]
    [InlineData(false, JobClass.C)]
    [InlineData(true, JobClass.A)]
    public void Classify_ForAStrongFitThatRequiresUnitedStatesWorkAuthorization_LiftsTheCapWhenTheCandidateHoldsIt(bool hasUnitedStatesWorkAuthorization, JobClass expected)
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 1, contractForm: 1, companySignal: 1,
            requiresUsAuthorization: true,
            hasUnitedStatesWorkAuthorization: hasUnitedStatesWorkAuthorization));

        Assert.Equal(expected, result.Class);
    }

    [Theory]
    [InlineData(false, JobClass.B)]
    [InlineData(true, JobClass.A)]
    public void Classify_ForAStrongFitWhoseOnlyOpenQuestionIsUnitedStatesAuthorization_BlocksClassAOnlyWithoutIt(bool hasUnitedStatesWorkAuthorization, JobClass expected)
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 1, contractForm: 1, companySignal: 1,
            blockingUnknowns: ["us_authorization"],
            hasUnitedStatesWorkAuthorization: hasUnitedStatesWorkAuthorization));

        Assert.Equal(expected, result.Class);
    }

    [Theory]
    [MemberData(nameof(ContractorParityCases))]
    public void Classify_ForAContractorWithoutUnitedStatesAuthorization_MatchesThePreviousRules(string employmentType, string[] blockingUnknowns, bool requiresUsAuthorization, int? compHeadline, int niche, int others, int modelCompSignal)
    {
        YearlyComp comp = compHeadline is int headline ? new YearlyComp(headline, headline) : YearlyComp.Unknown;
        ClassificationInput input = Input(
            niche: niche, level: others, stack: others, remoteTimezone: others, contractForm: others, companySignal: others,
            modelCompSignal: modelCompSignal,
            employmentType: employmentType,
            blockingUnknowns: blockingUnknowns,
            requiresUsAuthorization: requiresUsAuthorization,
            comp: comp,
            minB2bHourly: 45m,
            minEmploymentAnnual: 65_000m,
            target: 120_000m);

        Assert.Equal(PreviousRules.Classify(input), Classifier.Classify(input));
    }

    public static TheoryData<string, string[], bool, int?, int, int, int> ContractorParityCases()
    {
        TheoryData<string, string[], bool, int?, int, int, int> cases = [];
        string[][] unknownSets = [[], ["b2b"], ["us_authorization"], ["end_client"], ["b2b", "timezone"]];

        foreach (string employmentType in (string[])["b2b", "employment", "either", "unknown"])
        {
            foreach (string[] unknowns in unknownSets)
            {
                foreach (bool requiresUsAuthorization in (bool[])[false, true])
                {
                    foreach (int? headline in (int?[])[null, 55_000, 70_000, 95_000, 140_000])
                    {
                        foreach ((int niche, int others) in ((int, int)[])[(0, 2), (1, 1), (2, 2), (2, 0)])
                        {
                            cases.Add(employmentType, unknowns, requiresUsAuthorization, headline, niche, others, 1);
                        }
                    }
                }
            }
        }

        return cases;
    }

    private static JobHunter.Domain.Settings NewSettings(ContractPreference preference, decimal? contractorHourly, decimal? employmentAnnual, decimal? target, bool hasUnitedStatesWorkAuthorization = false)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureCompensation(contractorHourly, employmentAnnual, target);
        settings.ConfigureCandidate(null, true, true, "en", "EUR", string.Empty, preference, hasUnitedStatesWorkAuthorization, null, settings.TitleIncludeTerms, settings.TitleExcludeTerms);

        return settings;
    }

    private static ClassificationInput Input(
        int niche = 1,
        int level = 1,
        int stack = 1,
        int remoteTimezone = 1,
        int contractForm = 1,
        int modelCompSignal = 1,
        int companySignal = 1,
        string employmentType = "b2b",
        string[]? blockingUnknowns = null,
        bool requiresUsAuthorization = false,
        bool? endClientNamed = null,
        YearlyComp? comp = null,
        bool prefilterDropped = false,
        decimal? minB2bHourly = null,
        decimal? minEmploymentAnnual = null,
        decimal? target = null,
        ContractPreference contractPreference = ContractPreference.Contractor,
        bool hasUnitedStatesWorkAuthorization = false)
    {
        return new ClassificationInput(
            new ScoreDimensionsPayload(niche, level, stack, remoteTimezone, contractForm, modelCompSignal, companySignal),
            employmentType,
            blockingUnknowns ?? [],
            requiresUsAuthorization,
            endClientNamed,
            comp ?? YearlyComp.Unknown,
            prefilterDropped,
            NewSettings(contractPreference, minB2bHourly, minEmploymentAnnual, target, hasUnitedStatesWorkAuthorization));
    }

    /// <summary>The classification rules as they stood before the contract preference and United States authorization settings, kept to prove a contractor without United States authorization classifies exactly as before.</summary>
    private static class PreviousRules
    {
        public static Classification Classify(ClassificationInput input)
        {
            decimal? minimum = string.Equals(input.EmploymentType.Trim(), "employment", StringComparison.OrdinalIgnoreCase)
                ? input.Settings.MinEmploymentAnnual
                : input.Settings.MinContractorHourly * 1760m;
            decimal? target = input.Settings.TargetAnnual;
            bool belowMinimum = minimum is decimal floor && input.Comp.Headline is decimal headline && headline < floor;

            int compSignal = minimum is null && target is null ? input.Scores.CompSignal
                : input.Comp.IsUnknown ? 1
                : belowMinimum ? 0
                : target is decimal wanted && input.Comp.Headline >= wanted ? 2 : 1;
            int total = input.Scores.Niche + input.Scores.Level + input.Scores.Stack + input.Scores.RemoteTimezone + input.Scores.ContractForm + compSignal + input.Scores.CompanySignal;

            JobClass rubricClass = belowMinimum ? JobClass.C
                : input.PrefilterDropped || total < 4 ? JobClass.D
                : total <= 6 ? JobClass.C
                : total <= 9 ? JobClass.B
                : input.Scores.Niche >= 1 && input.BlockingUnknowns.Count == 0 ? JobClass.A : JobClass.B;

            return new Classification(compSignal, total, input.RequiresUsAuthorization && rubricClass < JobClass.C ? JobClass.C : rubricClass);
        }
    }
}
