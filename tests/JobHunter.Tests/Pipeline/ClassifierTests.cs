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
        Classification result = Classifier.Classify(Input(comp: new EurYearComp(120_000m, 140_000m), minB2bHourly: 45m, target: 120_000m));

        Assert.Equal(2, result.CompSignal);
    }

    [Fact]
    public void Classify_WhenCompIsNotStated_ScoresTheCompensationSignalNeutral()
    {
        Classification result = Classifier.Classify(Input(modelCompSignal: 2, comp: EurYearComp.Unknown, minB2bHourly: 45m, target: 120_000m));

        Assert.Equal(1, result.CompSignal);
    }

    [Fact]
    public void Classify_WhenStatedCompSitsBetweenTheMinimumAndTheTarget_ScoresTheCompensationSignalNeutral()
    {
        Classification result = Classifier.Classify(Input(modelCompSignal: 0, comp: new EurYearComp(95_000m, 105_000m), minB2bHourly: 45m, target: 120_000m));

        Assert.Equal(1, result.CompSignal);
    }

    [Fact]
    public void Classify_WhenStatedCompIsBelowTheMinimum_ScoresTheCompensationSignalZero()
    {
        Classification result = Classifier.Classify(Input(modelCompSignal: 2, comp: new EurYearComp(40_000m, 55_000m), minB2bHourly: 45m, target: 120_000m));

        Assert.Equal(0, result.CompSignal);
    }

    [Fact]
    public void Classify_WhenNoCompensationBoundIsConfigured_KeepsTheSignalTheModelGave()
    {
        Classification result = Classifier.Classify(Input(modelCompSignal: 2, comp: new EurYearComp(30_000m, 30_000m)));

        Assert.Equal(2, result.CompSignal);
    }

    [Fact]
    public void Classify_WhenStatedCompIsBelowTheMinimum_ReturnsClassCWhateverTheTotal()
    {
        Classification result = Classifier.Classify(Input(
            niche: 2, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, companySignal: 2,
            comp: new EurYearComp(40_000m, 55_000m),
            minB2bHourly: 45m));

        Assert.Equal(JobClass.C, result.Class);
    }

    [Fact]
    public void Classify_ForAnEmploymentRole_ReadsTheMinimumAgainstTheAnnualEmploymentBound()
    {
        Classification result = Classifier.Classify(Input(
            employmentType: "employment",
            comp: new EurYearComp(50_000m, 50_000m),
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
            comp: new EurYearComp(95_000m, 95_000m),
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
    public void MinimumEurPerYear_ForAnEmploymentType_ReadsTheBoundThatApplies(string employmentType, int expected)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureCompensation(45m, 65_000m, 120_000m);

        Assert.Equal((decimal)expected, Classifier.MinimumEurPerYear(employmentType, settings));
    }

    [Fact]
    public void MinimumEurPerYear_WhenNoBoundIsConfigured_IsUnknown()
    {
        Assert.Null(Classifier.MinimumEurPerYear("b2b", JobHunter.Domain.Settings.CreateDefault()));
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
        EurYearComp? comp = null,
        bool prefilterDropped = false,
        decimal? minB2bHourly = null,
        decimal? minEmploymentAnnual = null,
        decimal? target = null)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureCompensation(minB2bHourly, minEmploymentAnnual, target);

        return new ClassificationInput(
            new ScoreDimensionsPayload(niche, level, stack, remoteTimezone, contractForm, modelCompSignal, companySignal),
            employmentType,
            blockingUnknowns ?? [],
            comp ?? EurYearComp.Unknown,
            prefilterDropped,
            settings);
    }
}
