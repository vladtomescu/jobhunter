using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Llm;
using JobHunter.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JobHunter.Applications;

/// <summary>Maps the endpoint that downloads a job's cover letter as a Word document.</summary>
public static class CoverLetterEndpoints
{
    /// <summary>The route of a job's cover letter document.</summary>
    public const string DocumentRoute = "/jobs/{jobId:guid}/cover-letter";

    /// <summary>The address a page links to for the job's cover letter document.</summary>
    public static string DocumentPath(Guid jobId)
    {
        return $"/jobs/{jobId}/cover-letter";
    }

    /// <summary>Maps the document endpoint.</summary>
    public static IEndpointRouteBuilder MapCoverLetterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(DocumentRoute, DownloadDocumentAsync);

        return endpoints;
    }

    /// <summary>Streams the job's cover letter as a .docx download, with the header from the saved settings and the template as they are now, named after the candidate and the company; 404 when the job has no letter.</summary>
    public static async Task<IResult> DownloadDocumentAsync(Guid jobId, JobQueryService jobQueries, SettingsService settingsService, PromptCatalog prompts, CancellationToken cancellationToken)
    {
        JobDetailView? view = await jobQueries.GetJobAsync(jobId, cancellationToken);
        if (view?.Application?.CoverLetter is not ApplicationCoverLetter coverLetter)
        {
            return Results.NotFound();
        }

        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        CoverLetterText letter = CoverLetterText.Compose(coverLetter, prompts.CoverLetterHeaderLines, settings);

        return TypedResults.File(CoverLetterDocument.Build(letter), CoverLetterDocument.ContentType, CoverLetterDocument.FileName(letter.Name, view.Job.Company));
    }
}
