using JobHunter.Domain;
using JobHunter.Llm.Contracts;

namespace JobHunter.Pipeline;

/// <summary>What classification needs: the dimensions the model scored, the facts that decide the compensation signal, and the bounds from the settings.</summary>
public sealed record ClassificationInput(
    ScoreDimensionsPayload Scores,
    string EmploymentType,
    IReadOnlyList<string> BlockingUnknowns,
    bool RequiresUsAuthorization,
    YearlyComp Comp,
    bool PrefilterDropped,
    Domain.Settings Settings);

/// <summary>The outcome of classification: the compensation signal as recomputed here, the total it belongs to, and the class.</summary>
public sealed record Classification(int CompSignal, int Total, JobClass Class);

/// <summary>Turns a scored job into a class; the model never decides the class, and the compensation bounds never reach the model.</summary>
public static class Classifier
{
    private const string EmploymentType = "employment";

    private const string ContractType = "b2b";

    private const string ContractBlockingUnknown = "b2b";

    private const string UnitedStatesAuthorizationBlockingUnknown = "us_authorization";

    /// <summary>Recomputes the compensation signal from the settings, sums the rubric and reads off the class.</summary>
    public static Classification Classify(ClassificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        decimal? minimum = MinimumPerYear(input.EmploymentType, input.Settings);
        decimal? target = input.Settings.TargetAnnual;
        bool belowMinimum = minimum is decimal floor && input.Comp.Headline is decimal headline && headline < floor;

        int compSignal = RecomputeCompSignal(input, minimum, target, belowMinimum);
        int total = Total(input.Scores, compSignal);

        return new Classification(compSignal, total, ReadClass(input, total, belowMinimum));
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

    private static int Total(ScoreDimensionsPayload scores, int compSignal)
    {
        return scores.Niche + scores.Level + scores.Stack + scores.RemoteTimezone + scores.ContractForm + compSignal + scores.CompanySignal;
    }

    /// <summary>Reads the class off the rubric, then holds a posting that requires United States work authorization at C when the candidate does not hold it: the applicant cannot take it, but a misread posting must stay findable rather than vanish.</summary>
    /// <remarks>The class runs A to D, so the weaker class is the greater value and the cap only ever moves a job down; a job the rubric already put at C or D keeps that class.</remarks>
    private static JobClass ReadClass(ClassificationInput input, int total, bool belowMinimum)
    {
        JobClass rubricClass = RubricClass(input, total, belowMinimum);
        bool authorizationBlocks = input.RequiresUsAuthorization && !input.Settings.HasUnitedStatesWorkAuthorization;

        return authorizationBlocks && rubricClass < JobClass.C ? JobClass.C : rubricClass;
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

        return input.Scores.Niche >= 1 && !input.BlockingUnknowns.Any(unknown => BlocksTheCandidate(unknown, input.Settings)) ? JobClass.A : JobClass.B;
    }

    /// <summary>Whether an open question in the posting matters to this candidate: whether a contract is possible matters only to a contractor, and whether United States authorization is needed only to a candidate who does not hold it; every other open question blocks.</summary>
    private static bool BlocksTheCandidate(string blockingUnknown, Domain.Settings settings)
    {
        return blockingUnknown.Trim().ToLowerInvariant() switch
        {
            ContractBlockingUnknown => settings.ContractPreference == ContractPreference.Contractor,
            UnitedStatesAuthorizationBlockingUnknown => !settings.HasUnitedStatesWorkAuthorization,
            _ => true
        };
    }
}
