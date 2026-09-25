using System.ComponentModel.DataAnnotations;
using JobHunter.Jobs;
using JobHunter.Refresh;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Pages;

/// <summary>Adds a posting no source covers, or edits one added earlier, through the same normalization and rules a fetched job goes through.</summary>
public partial class AddJob
{
    private ManualJobEntry form = new();

    private string? notEditable;

    private string? refusal;

    private bool busy;

    private bool formLoaded;

    private Guid? formLoadedFor;

    /// <summary>The manual job being edited; absent when adding a new one.</summary>
    [Parameter]
    public Guid? Id { get; set; }

    [Inject]
    private ManualJobService ManualJobs { get; set; } = null!;

    [Inject]
    private JobQueryService JobQueries { get; set; } = null!;

    [Inject]
    private ScoreBacklog Backlog { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    private string Heading => Id is null ? "Add job" : "Edit job";

    private string LeadIn => Id is null
        ? "Paste the link and the posting text; the next score run scores it like any other job."
        : "Change what was typed for this job; a new description sends it back to the next score run.";

    private string SubmitLabel => Id is null ? "Add job" : "Save";

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (formLoaded && formLoadedFor == Id)
        {
            return;
        }

        formLoaded = true;
        formLoadedFor = Id;
        form = new ManualJobEntry();
        notEditable = null;
        refusal = null;

        if (Id is not Guid jobId)
        {
            return;
        }

        JobDetailView? view = await JobQueries.GetJobAsync(jobId);
        if (view is null)
        {
            notEditable = "No job is stored under that identifier.";

            return;
        }

        if (!view.Job.IsManual)
        {
            notEditable = "Only a job entered by hand can be edited; a source owns the facts of this one.";

            return;
        }

        form = ManualJobEntry.From(ManualJobRequest.FromJob(view.Job));
    }

    private async Task SubmitAsync()
    {
        busy = true;
        refusal = null;

        try
        {
            ManualJobRequest request = form.ToRequest();

            if (Id is Guid jobId)
            {
                ManualJobEditResult edit = await ManualJobs.UpdateAsync(jobId, request);
                if (!edit.IsSaved)
                {
                    refusal = edit.Refusal;

                    return;
                }

                await Backlog.RecountAsync();
                Navigation.NavigateTo($"/jobs/{jobId}");

                return;
            }

            ManualJobResult result = await ManualJobs.CreateAsync(request);

            await Backlog.RecountAsync();
            Navigation.NavigateTo($"/jobs/{result.JobId}");
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>What the add and edit form collects before it becomes a job.</summary>
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

        /// <summary>The form filled with a stored entry, for editing.</summary>
        public static ManualJobEntry From(ManualJobRequest request)
        {
            return new ManualJobEntry
            {
                ApplyUrl = request.ApplyUrl,
                Title = request.Title,
                Company = request.Company,
                LocationText = request.LocationText,
                CompText = request.CompText,
                DescriptionText = request.DescriptionText
            };
        }

        /// <summary>The request the manual job service takes.</summary>
        public ManualJobRequest ToRequest()
        {
            return new ManualJobRequest(ApplyUrl, Title, Company, DescriptionText, LocationText, CompText);
        }
    }
}
