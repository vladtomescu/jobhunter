using System.ComponentModel.DataAnnotations;
using JobHunter.Jobs;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Pages;

/// <summary>Adds a posting no source covers, through the same normalization and rules a fetched job goes through.</summary>
public partial class AddJob
{
    private readonly ManualJobEntry form = new();

    private bool busy;

    [Inject]
    private ManualJobService ManualJobs { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task SubmitAsync()
    {
        busy = true;

        try
        {
            ManualJobResult result = await ManualJobs.CreateAsync(new ManualJobRequest(
                form.ApplyUrl,
                form.Title,
                form.Company,
                form.DescriptionText,
                form.LocationText,
                form.CompText));

            Navigation.NavigateTo($"/jobs/{result.JobId}");
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>What the add form collects before it becomes a job.</summary>
    public sealed class ManualJobEntry
    {
        [Required]
        [Url]
        public string ApplyUrl { get; set; } = string.Empty;

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Company { get; set; } = string.Empty;

        public string? LocationText { get; set; }

        public string? CompText { get; set; }

        public string? DescriptionText { get; set; }
    }
}
