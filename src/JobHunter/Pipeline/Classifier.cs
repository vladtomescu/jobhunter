using JobHunter.Domain;
using JobHunter.Llm.Contracts;

namespace JobHunter.Pipeline;

/// <summary>What classification needs: the dimensions the model scored, the facts that decide the compensation signal, and the bounds from the settings.</summary>
public sealed record ClassificationInput(
    ScoreDimensionsPayload Scores,
    string EmploymentType,
    IReadOnlyList<string> BlockingUnknowns,
    bool RequiresUsAuthorization,
    EurYearComp Comp,
    bool PrefilterDropped,
    Domain.Settings Settings);

/// <summary>The outcome of classification: the compensation signal as recomputed here, the total it belongs to, and the class.</summary>
public sealed record Classification(int CompSignal, int Total, JobClass Class);

/// <summary>Turns a scored job into a class; the model never decides the class, and the compensation bounds never reach the model.</summary>
public static class Classifier
{
    /// <summary>Recomputes the compensation signal from the settings, sums the rubric and reads off the class.</summary>
    public static Classification Classify(ClassificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        decimal? minimum = MinimumEurPerYear(input.EmploymentType, input.Settings);
        decimal? target = input.Settings.TargetAnnual;
        bool belowMinimum = minimum is decimal floor && input.Comp.Headline is decimal headline && headline < floor;

        int compSignal = RecomputeCompSignal(input, minimum, target, belowMinimum);
        int total = Total(input.Scores, compSignal);

        return new Classification(compSignal, total, ReadClass(input, total, belowMinimum));
    }

    /// <summary>The yearly compensation a posting has to reach: the business-to-business hourly minimum annualized, or the employment minimum when the role is employment only.</summary>
    /// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
    public static decimal? MinimumEurPerYear(string? employmentType, Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return string.Equals(employmentType?.Trim(), "employment", StringComparison.OrdinalIgnoreCase)
            ? settings.MinEmploymentAnnual
            : settings.MinContractorHourly * CompNormalizer.HoursPerYear;
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

    /// <summary>Reads the class off the rubric, then holds a posting that requires United States work authorization at C: the applicant cannot take it, but a misread posting must stay findable rather than vanish.</summary>
    /// <remarks>The class runs A to D, so the weaker class is the greater value and the cap only ever moves a job down; a job the rubric already put at C or D keeps that class.</remarks>
    private static JobClass ReadClass(ClassificationInput input, int total, bool belowMinimum)
    {
        JobClass rubricClass = RubricClass(input, total, belowMinimum);

        return input.RequiresUsAuthorization && rubricClass < JobClass.C ? JobClass.C : rubricClass;
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

        return input.Scores.Niche >= 1 && input.BlockingUnknowns.Count == 0 ? JobClass.A : JobClass.B;
    }
}
