using JobHunter.Domain;

namespace JobHunter.Jobs;

/// <summary>One rubric dimension of a score as the pages draw it: its name, the points it earned, and one pip per point it could earn.</summary>
public sealed record ScoreDimension(string Name, int Points)
{
    /// <summary>One entry per point the dimension can earn, first point first, true where the point was earned.</summary>
    public IReadOnlyList<bool> Pips => [.. Enumerable.Range(1, ScorePoints.MaximumPerDimension).Select(point => Points >= point)];
}

/// <summary>The points of the seven rubric dimensions of one score, which the score ribbon draws as a 7 x 2 grid of pips.</summary>
public sealed record ScorePoints(int Niche, int Level, int Stack, int RemoteTimezone, int ContractForm, int CompSignal, int CompanySignal)
{
    /// <summary>The most points one dimension can earn.</summary>
    public const int MaximumPerDimension = 2;

    /// <summary>The seven dimensions in the order the rubric lists them, which is the order the ribbon draws them.</summary>
    public IReadOnlyList<ScoreDimension> Dimensions =>
    [
        new("Niche", Niche),
        new("Level", Level),
        new("Stack", Stack),
        new("Remote and timezone", RemoteTimezone),
        new("Contract form", ContractForm),
        new("Compensation signal", CompSignal),
        new("Company signal", CompanySignal)
    ];

    /// <summary>The dimension points of a stored score card.</summary>
    public static ScorePoints From(ScoreCard score)
    {
        ArgumentNullException.ThrowIfNull(score);

        return new ScorePoints(score.Niche, score.Level, score.Stack, score.RemoteTimezone, score.ContractForm, score.CompSignal, score.CompanySignal);
    }
}
