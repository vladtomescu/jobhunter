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
    public void Classify_ForAStrongTotalOutsideTheNiche_ReturnsClassA()
    {
        Classification result = Classifier.Classify(Input(niche: 0, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2));

        Assert.Equal(11, result.Total);
        Assert.Equal(JobClass.A, result.Class);
    }

    [Fact]
    public void Classify_ForAStrongTotalOutsideTheNicheWithNicheAndStackBelowTwo_ReturnsClassC()
    {
        Classification result = Classifier.Classify(Input(niche: 0, level: 2, stack: 1, remoteTimezone: 2, contractForm: 2, companySignal: 2));

        Assert.Equal(10, result.Total);
        Assert.Equal(JobClass.C, result.Class);
    }

    [Theory]
    [InlineData(ContractPreference.Contractor)]
    [InlineData(ContractPreference.Employee)]
    [InlineData(ContractPreference.Either)]
    public void Classify_ForAStrongFitWhoseOnlyOpenQuestionIsTheHours_ReturnsClassA(ContractPreference preference)
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 1, contractForm: 1, companySignal: 1,
            blockingUnknowns: ["timezone"],
            contractPreference: preference));

        Assert.Equal(JobClass.A, result.Class);
    }

    [Theory]
    [InlineData("timezone", ContractPreference.Contractor, false)]
    [InlineData("b2b", ContractPreference.Contractor, true)]
    [InlineData("b2b", ContractPreference.Employee, false)]
    [InlineData("end_client", ContractPreference.Either, true)]
    public void BlocksTheCandidate_ForAnOpenQuestion_TellsWhetherItHoldsBackClassA(string blockingUnknown, ContractPreference preference, bool expected)
    {
        Assert.Equal(expected, Classifier.BlocksTheCandidate(blockingUnknown, NewSettings(preference, 45m, 65_000m, null)));
    }

    [Fact]
    public void Classify_ForAStrongFitWithOpenHoursAndAnUnnamedEmployer_ReturnsClassB()
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 1, contractForm: 1, companySignal: 1,
            blockingUnknowns: ["timezone", "end_client"]));

        Assert.Equal(JobClass.B, result.Class);
    }

    [Fact]
    public void Classify_ForAStrongFitWithOpenHoursThatRequiresUnitedStatesWorkAuthorization_ReturnsClassC()
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 1, contractForm: 1, companySignal: 1,
            blockingUnknowns: ["timezone"],
            requiresUsAuthorization: true));

        Assert.Equal(JobClass.C, result.Class);
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

    [Fact]
    public void Classify_ForAStrongFitWhoseCvLacksThePrimarySkill_ScoresTheStackZeroAndHoldsTheClassAtC()
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2,
            coverage: Coverage("primary:no", "required:yes")));

        Assert.Equal(0, result.Stack);
        Assert.Equal(11, result.Total);
        Assert.Equal(JobClass.C, result.Class);
    }

    [Fact]
    public void Classify_ForAWeakFitWhoseCvLacksThePrimarySkill_KeepsClassD()
    {
        Classification result = Classifier.Classify(Input(
            niche: 0, level: 0, stack: 2, remoteTimezone: 0, contractForm: 0, companySignal: 0,
            coverage: Coverage("primary:no")));

        Assert.Equal(JobClass.D, result.Class);
    }

    [Theory]
    [InlineData(new[] { "primary:yes", "primary:no", "primary:no", "primary:no", "primary:no" }, 0)]
    [InlineData(new[] { "primary:no", "primary:unjudged", "primary:no" }, 1)]
    [InlineData(new[] { "primary:no", "primary:close", "primary:yes" }, 0)]
    public void Classify_ForAStrongFitWhoseCvCoversOneOfTheAlternativePrimarySkills_PassesThePrimarySideWithoutLoweringTheStack(string[] skills, int modelStack)
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: modelStack, remoteTimezone: 2, contractForm: 2, companySignal: 2,
            coverage: Coverage(skills)));

        Assert.Equal(2, result.Stack);
        Assert.Equal(JobClass.A, result.Class);
    }

    [Fact]
    public void Classify_ForAStrongFitWhoseCvLacksEveryAlternativePrimarySkill_ScoresTheStackZeroAndHoldsTheClassAtC()
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2,
            coverage: Coverage("primary:no", "primary:no", "primary:no", "required:yes")));

        Assert.Equal(0, result.Stack);
        Assert.Equal(JobClass.C, result.Class);
    }

    [Fact]
    public void Classify_ForAStrongFitWhoseCvOnlyComesCloseToOneAlternativePrimarySkill_ScoresTheStackOneWithoutTheCap()
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2,
            coverage: Coverage("primary:close", "primary:no", "required:yes")));

        Assert.Equal(1, result.Stack);
        Assert.Equal(JobClass.A, result.Class);
    }

    [Fact]
    public void Classify_ForAlternativePrimarySkillsTheCvCoversButMostRequiredSkillsItLacks_StillHoldsTheClassAtC()
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2,
            coverage: Coverage("primary:yes", "primary:no", "required:no", "required:no", "required:yes")));

        Assert.Equal(0, result.Stack);
        Assert.Equal(JobClass.C, result.Class);
    }

    [Theory]
    [InlineData(new[] { "primary:close", "required:yes" }, 2, 1)]
    [InlineData(new[] { "primary:yes", "required:no", "required:no", "required:yes", "required:yes" }, 2, 1)]
    [InlineData(new[] { "primary:yes", "required:no", "required:close" }, 0, 2)]
    [InlineData(new[] { "primary:yes", "nice_to_have:no", "nice_to_have:no" }, 0, 2)]
    [InlineData(new[] { "required:yes", "required:close" }, 1, 2)]
    [InlineData(new[] { "primary:unjudged", "required:unjudged", "required:unjudged" }, 0, 2)]
    public void Classify_ForTheSkillsACardNames_RecomputesTheStackFromTheCvMarks(string[] skills, int modelStack, int expected)
    {
        Classification result = Classifier.Classify(Input(niche: 2, stack: modelStack, coverage: Coverage(skills)));

        Assert.Equal(expected, result.Stack);
        Assert.Equal(7 + expected, result.Total);
    }

    [Theory]
    [InlineData(new[] { "required:no", "required:no", "required:yes", "required:yes" }, 1, JobClass.A)]
    [InlineData(new[] { "required:no", "required:no", "required:no", "required:yes" }, 0, JobClass.C)]
    [InlineData(new[] { "required:no", "required:no" }, 0, JobClass.C)]
    [InlineData(new[] { "required:no" }, 2, JobClass.A)]
    [InlineData(new[] { "required:no", "required:no", "required:unjudged" }, 0, JobClass.C)]
    [InlineData(new[] { "required:no", "required:no", "required:unjudged", "nice_to_have:yes", "nice_to_have:yes" }, 0, JobClass.C)]
    public void Classify_ForAStrongFitAndTheRequiredSkillsTheCvLacks_ScoresTheStackZeroAndHoldsTheClassAtCOnlyWhenItLacksMostOfThem(string[] skills, int expectedStack, JobClass expected)
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2,
            coverage: Coverage(skills)));

        Assert.Equal(expectedStack, result.Stack);
        Assert.Equal(expected, result.Class);
    }

    [Fact]
    public void Classify_ForRequiredSkillsNamedTwiceUnderTwoNames_CountsEachOnceTowardsTheRequiredTotal()
    {
        Skill go = Skill.CreateFromModel("Go", SkillCategory.Language, CvMark.No);
        go.Aliases.Add("golang");
        Skill rust = Skill.CreateFromModel("Rust", SkillCategory.Language, CvMark.No);
        ScoreSkill[] named =
        [
            new("Go", "required", "language", "no"),
            new("golang", "required", "language", "no"),
            new("Rust", "required", "language", "no"),
            new("Elixir", "required", "language", "no"),
            new("elixir ", "required", "language", "no")
        ];

        SkillCoverage? coverage = SkillCoverage.Of(named, new SkillCatalog([go, rust]));
        Classification result = Classifier.Classify(Input(niche: 2, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2, coverage: coverage));

        Assert.Equal(new SkillCoverage(0, 0, 0, 2, 3), coverage);
        Assert.Equal(0, result.Stack);
        Assert.Equal(JobClass.C, result.Class);
    }

    [Fact]
    public void Classify_WhenTheCardNamesNoSkill_KeepsTheModelsStackWithoutTheSkillCap()
    {
        SkillCoverage? coverage = SkillCoverage.Of([], new SkillCatalog([]));

        Classification result = Classifier.Classify(Input(niche: 2, level: 2, stack: 0, remoteTimezone: 2, contractForm: 2, companySignal: 1, coverage: coverage));

        Assert.Null(coverage);
        Assert.Equal(0, result.Stack);
        Assert.Equal(JobClass.A, result.Class);
    }

    [Fact]
    public void Classify_ForAStrongTotalOutsideTheNicheWhoseCvComesCloseToThePrimarySkill_ReadsTheThinFitOffTheRecomputedStack()
    {
        Classification result = Classifier.Classify(Input(
            niche: 0, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2,
            coverage: Coverage("primary:close")));

        Assert.Equal(10, result.Total);
        Assert.Equal(JobClass.C, result.Class);
    }

    [Fact]
    public void Classify_ForARequiredSkillNamedTwiceUnderTwoNames_CountsItOnce()
    {
        Skill python = Skill.CreateFromModel("Python", SkillCategory.Language, CvMark.No);
        python.Aliases.Add("py");
        ScoreSkill[] named = [new("Python", "required", "language", "no"), new("py", "required", "language", "no")];

        Classification result = Classifier.Classify(Input(niche: 2, stack: 0, coverage: SkillCoverage.Of(named, new SkillCatalog([python]))));

        Assert.Equal(2, result.Stack);
    }

    [Fact]
    public void Classify_WhenTheCatalogMarkDiffersFromTheModelsJudgementOnTheCard_ReadsTheCatalogMark()
    {
        Skill dotnet = Skill.CreateFromModel(".NET", SkillCategory.Framework, CvMark.Yes);
        ScoreSkill[] named = [new("dotnet", "primary", "framework", "no")];

        Classification result = Classifier.Classify(Input(niche: 2, stack: 0, coverage: SkillCoverage.Of(named, new SkillCatalog([dotnet]))));

        Assert.Equal(2, result.Stack);
        Assert.Equal(JobClass.B, result.Class);
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
        string[][] unknownSets = [[], ["b2b"], ["us_authorization"], ["end_client"], ["timezone"], ["b2b", "timezone"]];

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
        settings.ConfigureCandidate(null, string.Empty, true, true, "en", "EUR", string.Empty, preference, hasUnitedStatesWorkAuthorization, null, settings.TitleIncludeTerms, settings.TitleExcludeTerms);

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
        YearlyComp? comp = null,
        bool prefilterDropped = false,
        decimal? minB2bHourly = null,
        decimal? minEmploymentAnnual = null,
        decimal? target = null,
        ContractPreference contractPreference = ContractPreference.Contractor,
        bool hasUnitedStatesWorkAuthorization = false,
        SkillCoverage? coverage = null)
    {
        return new ClassificationInput(
            new ScoreDimensionsPayload(niche, level, stack, remoteTimezone, contractForm, modelCompSignal, companySignal),
            employmentType,
            blockingUnknowns ?? [],
            requiresUsAuthorization,
            comp ?? YearlyComp.Unknown,
            prefilterDropped,
            NewSettings(contractPreference, minB2bHourly, minEmploymentAnnual, target, hasUnitedStatesWorkAuthorization),
            coverage);
    }

    /// <summary>The coverage of a card naming one distinct skill per entry, each written as importance:mark; a skill marked unjudged is left out of the catalog, the way a skill nobody judged yet counts.</summary>
    private static SkillCoverage? Coverage(params string[] skills)
    {
        List<ScoreSkill> named = [];
        List<Skill> judged = [];

        foreach ((string entry, int index) in skills.Select((entry, index) => (entry, index)))
        {
            string[] parts = entry.Split(':');
            string name = $"Skill {index}";
            named.Add(new ScoreSkill(name, parts[0], "tool", "yes"));

            if (parts[1] != "unjudged")
            {
                judged.Add(Skill.CreateFromModel(name, SkillCategory.Tool, Enum.Parse<CvMark>(parts[1], ignoreCase: true)));
            }
        }

        return SkillCoverage.Of(named, new SkillCatalog(judged));
    }

    /// <summary>The classification rules as they stood before the contract preference and United States authorization settings, with the later class A rule that has no Niche floor and never blocks on the hours, kept to prove a contractor without United States authorization classifies exactly as before.</summary>
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
                : input.BlockingUnknowns.All(unknown => unknown == "timezone") ? JobClass.A : JobClass.B;

            return new Classification(compSignal, input.Scores.Stack, total, input.RequiresUsAuthorization && rubricClass < JobClass.C ? JobClass.C : rubricClass);
        }
    }
}
