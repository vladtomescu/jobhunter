using JobHunter.Jobs;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Shared;

/// <summary>The score ribbon: the points of the seven rubric dimensions as pips, compact for an inbox row or labelled for the job page.</summary>
public sealed partial class ScoreRibbon
{
    /// <summary>The dimension points to draw.</summary>
    [Parameter]
    [EditorRequired]
    public ScorePoints Points { get; set; } = null!;

    /// <summary>True to list the dimensions by name with their pips, as the job page does; false for the compact 7 x 2 ribbon of the inbox.</summary>
    [Parameter]
    public bool Labelled { get; set; }

    /// <summary>Every dimension with its points, read by assistive technology and shown on hover over the compact ribbon.</summary>
    private string Summary => string.Join(" · ", Points.Dimensions.Select(dimension => $"{dimension.Name} {dimension.Points}"));
}
