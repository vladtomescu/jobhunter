using JobHunter.Domain;
using JobHunter.Llm.Contracts;

namespace JobHunter.Pipeline;

/// <summary>What classification needs: the dimensions the model scored, the facts that decide the compensation signal, the authorization cap, whether the candidate accepts the job's place, the bounds from the settings, and how far the CV covers the posting's skills, null when the card names no skill.</summary>
/// <remarks><see cref="PlaceAccepted"/> is true when a remote role in the job's place is open to the candidate, as <see cref="GeographyRules.IsPlaceAccepted"/> reads it.</remarks>
public sealed record ClassificationInput(
    ScoreDimensionsPayload Scores,
    string EmploymentType,
    IReadOnlyList<string> BlockingUnknowns,
    bool RequiresUsAuthorization,
    bool PlaceAccepted,
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

    /// <summary>Recomputes the compensation signal from the settings and the stack from the skill coverage, sums the rubric and reads off the class; a role the rubric scores out of reach on remote and timezone is class D unless its pay is below the minimum, which holds it at C, and a role whose place the candidate does not accept is class D unless the rubric scored it open to the candidate's region.</summary>
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

    /// <summary>Scores the stack as the lower of the stack the model gave and the one the CV check reads off the skills, the way an AI ranker reads the required qualifications: 0 when the CV lacks every primary skill or most of the required skills, 1 when it covers none of the primary skills but comes close to one, lacks two or more required skills or the card names no primary skill, 2 otherwise; a card that names no skill keeps the stack the model gave.</summary>
    /// <remarks>The primary skills are alternatives, so one covered primary skill passes the primary side and the ones the CV lacks do not lower the stack. A posting that names no language leaves the stack unstated, which the rubric scores 1, so the required skills alone never raise the stack to 2. The model's stack applies the rubric to what the skills cannot show, infrastructure built on tools the candidate does not use for example, so the CV check only ever lowers it; the scores carry the model's stack, never the recomputed one a card stores, so a recompute follows the CV marks both ways.</remarks>
    private static int RecomputeStack(ClassificationInput input)
    {
        if (input.SkillCoverage is not SkillCoverage coverage)
        {
            return input.Scores.Stack;
        }

        int fromSkills = FailsTheCvGate(coverage)
            ? 0
            : coverage.PrimaryTotal == 0 || coverage.OnlyComesCloseToThePrimarySkills || coverage.RequiredMarkedNo >= 2 ? 1 : 2;

        return Math.Min(input.Scores.Stack, fromSkills);
    }

    private static int Total(ScoreDimensionsPayload scores, int stack, int compSignal)
    {
        return scores.Niche + scores.Level + stack + scores.RemoteTimezone + scores.ContractForm + compSignal + scores.CompanySignal;
    }

    /// <summary>Puts a job whose place the candidate does not accept at D unless the rubric scored it remote and open to the candidate's region; otherwise reads the class off the rubric, then holds a posting at C when it requires United States work authorization the candidate does not hold, when the CV lacks every one of its primary skills or most of its required skills, or when niche and stack together score below 2: a capped posting stays findable rather than vanishing.</summary>
    /// <remarks>The class runs A to D, so the weaker class is the greater value and the caps only ever move a job down; a job the rubric already put at C or D keeps that class. A remote and timezone score of 2 is the rubric's only reading of remote and open to the candidate's region, whatever the hours: a lower score leaves a job in a place the candidate does not accept remote for its own residents only, remote with only a foreign office named, hybrid or on-site.</remarks>
    private static JobClass ReadClass(ClassificationInput input, int stack, int total, bool belowMinimum)
    {
        if (!input.PlaceAccepted && input.Scores.RemoteTimezone < 2)
        {
            return JobClass.D;
        }

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

    /// <summary>Reads the class off the rubric total: C below the pay minimum, D for a posting the prefilter dropped or one out of reach, then by the total.</summary>
    /// <remarks>A remote and timezone score of 0 means the rubric found the role out of reach: on-site or hybrid where relocation is refused, a work authorization the candidate does not hold with no sponsorship, or a region outside the targets. The model does not apply that judgement the same way on every run, so the class follows the score rather than the total.</remarks>
    private static JobClass RubricClass(ClassificationInput input, int total, bool belowMinimum)
    {
        if (belowMinimum)
        {
            return JobClass.C;
        }

        if (input.PrefilterDropped || input.Scores.RemoteTimezone == 0 || total < 4)
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
