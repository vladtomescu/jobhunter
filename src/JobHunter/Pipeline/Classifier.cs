using JobHunter.Domain;
using JobHunter.Llm.Contracts;

namespace JobHunter.Pipeline;

/// <summary>What classification needs: the dimensions the model scored, the facts that decide the compensation signal and the authorization cap, the bounds from the settings, and how far the CV covers the posting's skills, null when the card names no skill.</summary>
public sealed record ClassificationInput(
    ScoreDimensionsPayload Scores,
    string EmploymentType,
    IReadOnlyList<string> BlockingUnknowns,
    bool RequiresUsAuthorization,
    YearlyComp Comp,
    bool PrefilterDropped,
    Domain.Settings Settings,
    SkillCoverage? SkillCoverage);

/// <summary>The outcome of classification: the compensation signal and the stack as recomputed here, the total they belong to, and the class.</summary>
public sealed record Classification(int CompSignal, int Stack, int Total, JobClass Class);

/// <summary>Turns a scored job into a class; the model never decides the class, and the compensation bounds never reach the model.</summary>
public static class Classifier
{
    private const string EmploymentType = "employment";

    private const string ContractType = "b2b";

    private const string ContractBlockingUnknown = "b2b";

    private const string UnitedStatesAuthorizationBlockingUnknown = "us_authorization";

    private const string HoursBlockingUnknown = "timezone";

    /// <summary>Recomputes the compensation signal from the settings and the stack from the skill coverage, sums the rubric and reads off the class.</summary>
    public static Classification Classify(ClassificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        decimal? minimum = MinimumPerYear(input.EmploymentType, input.Settings);
        decimal? target = input.Settings.TargetAnnual;
        bool belowMinimum = minimum is decimal floor && input.Comp.Headline is decimal headline && headline < floor;

        int compSignal = RecomputeCompSignal(input, minimum, target, belowMinimum);
        int stack = RecomputeStack(input);
        int total = Total(input.Scores, stack, compSignal);

        return new Classification(compSignal, stack, total, ReadClass(input, stack, total, belowMinimum));
    }

    /// <summary>The yearly compensation, in the base currency, a posting has to reach: the employment minimum for an employment role, the contractor hourly minimum annualized for a contract role, and for a posting that offers either or does not say, the minimum of the contract form the candidate prefers.</summary>
    /// <remarks>A candidate who takes either form is held to the lower of the two minimums when both are set, since either offer would do; with one set, that one applies. The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
    public static decimal? MinimumPerYear(string? employmentType, Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        decimal? contractorMinimum = settings.MinContractorHourly * CompNormalizer.HoursPerYear;
        decimal? employmentMinimum = settings.MinEmploymentAnnual;

        return employmentType?.Trim().ToLowerInvariant() switch
        {
            EmploymentType => employmentMinimum,
            ContractType => contractorMinimum,
            _ => settings.ContractPreference switch
            {
                ContractPreference.Contractor => contractorMinimum,
                ContractPreference.Employee => employmentMinimum,
                _ => Lower(contractorMinimum, employmentMinimum)
            }
        };
    }

    private static decimal? Lower(decimal? first, decimal? second)
    {
        return first is decimal one && second is decimal other ? Math.Min(one, other) : first ?? second;
    }

    private static int RecomputeCompSignal(ClassificationInput input, decimal? minimum, decimal? target, bool belowMinimum)
    {
        if (minimum is null && target is null)
        {
            return input.Scores.CompSignal;
        }

        if (input.Comp.IsUnknown)
        {
            return 1;
        }

        if (belowMinimum)
        {
            return 0;
        }

        return target is decimal wanted && input.Comp.Headline >= wanted ? 2 : 1;
    }

    /// <summary>Scores the stack from what the CV covers, the way an AI ranker reads the required qualifications: 0 when the CV lacks every primary skill or most of the required skills, 1 when it covers none of the primary skills but comes close to one or lacks two or more required skills, 2 otherwise; a card that names no skill keeps the stack the model gave.</summary>
    /// <remarks>The primary skills are alternatives, so one covered primary skill passes the primary side and the ones the CV lacks do not lower the stack.</remarks>
    private static int RecomputeStack(ClassificationInput input)
    {
        if (input.SkillCoverage is not SkillCoverage coverage)
        {
            return input.Scores.Stack;
        }

        if (FailsTheCvGate(coverage))
        {
            return 0;
        }

        return coverage.OnlyComesCloseToThePrimarySkills || coverage.RequiredMarkedNo >= 2 ? 1 : 2;
    }

    private static int Total(ScoreDimensionsPayload scores, int stack, int compSignal)
    {
        return scores.Niche + scores.Level + stack + scores.RemoteTimezone + scores.ContractForm + compSignal + scores.CompanySignal;
    }

    /// <summary>Reads the class off the rubric, then holds a posting at C when it requires United States work authorization the candidate does not hold, when the CV lacks every one of its primary skills or most of its required skills, or when niche and stack together score below 2: a capped posting stays findable rather than vanishing.</summary>
    /// <remarks>The class runs A to D, so the weaker class is the greater value and the caps only ever move a job down; a job the rubric already put at C or D keeps that class.</remarks>
    private static JobClass ReadClass(ClassificationInput input, int stack, int total, bool belowMinimum)
    {
        JobClass rubricClass = RubricClass(input, total, belowMinimum);
        bool authorizationBlocks = input.RequiresUsAuthorization && !input.Settings.HasUnitedStatesWorkAuthorization;
        bool cvGateFails = input.SkillCoverage is SkillCoverage coverage && FailsTheCvGate(coverage);
        bool capped = authorizationBlocks || cvGateFails || FitIsTooThin(input.Scores.Niche, stack);

        return capped && rubricClass < JobClass.C ? JobClass.C : rubricClass;
    }

    /// <summary>Whether an AI ranker would screen the CV out: it lacks every primary skill, or most of the required skills.</summary>
    private static bool FailsTheCvGate(SkillCoverage coverage)
    {
        return coverage.LacksEveryPrimarySkill || coverage.LacksMostRequiredSkills;
    }

    private static bool FitIsTooThin(int niche, int stack)
    {
        return niche + stack < 2;
    }

    private static JobClass RubricClass(ClassificationInput input, int total, bool belowMinimum)
    {
        if (belowMinimum)
        {
            return JobClass.C;
        }

        if (input.PrefilterDropped || total < 4)
        {
            return JobClass.D;
        }

        if (total <= 6)
        {
            return JobClass.C;
        }

        if (total <= 9)
        {
            return JobClass.B;
        }

        return input.BlockingUnknowns.Any(unknown => BlocksTheCandidate(unknown, input.Settings)) ? JobClass.B : JobClass.A;
    }

    /// <summary>Whether an open question in the posting matters to this candidate: whether a contract is possible matters only to a contractor, whether United States authorization is needed only to a candidate who does not hold it, and the working hours never, since remote and timezone already scores them; every other open question blocks.</summary>
    public static bool BlocksTheCandidate(string blockingUnknown, Domain.Settings settings)
    {
        return blockingUnknown.Trim().ToLowerInvariant() switch
        {
            ContractBlockingUnknown => settings.ContractPreference == ContractPreference.Contractor,
            UnitedStatesAuthorizationBlockingUnknown => !settings.HasUnitedStatesWorkAuthorization,
            HoursBlockingUnknown => false,
            _ => true
        };
    }
}
