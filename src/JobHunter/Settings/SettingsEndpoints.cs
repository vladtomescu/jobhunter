using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JobHunter.Settings;

/// <summary>The saved path and content type for one resume kind; never built from anything the caller sends.</summary>
public sealed record ResumeFileSelection(string Path, string ContentType);

/// <summary>Resolves a resume kind to the matching saved settings path, without ever taking a path from the caller.</summary>
public static class ResumeFileSelector
{
    /// <summary>Route segment naming the PDF resume.</summary>
    public const string PdfKind = "pdf";

    /// <summary>Route segment naming the markdown resume.</summary>
    public const string MarkdownKind = "markdown";

    /// <summary>Resolves the kind to the saved settings path and its content type; an unrecognized kind resolves to nothing.</summary>
    public static ResumeFileSelection? Select(Domain.Settings settings, string kind)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return kind switch
        {
            PdfKind => new ResumeFileSelection(settings.ResumePdfPath, "application/pdf"),
            MarkdownKind => new ResumeFileSelection(settings.ResumeMarkdownPath, "text/plain; charset=utf-8"),
            _ => null
        };
    }
}

/// <summary>Maps the settings module's HTTP endpoints.</summary>
public static class SettingsEndpoints
{
    /// <summary>Maps the endpoint that streams the resume saved in settings, selected only by kind.</summary>
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/settings/resume/{kind}", StreamResumeAsync);

        return endpoints;
    }

    /// <summary>Streams the saved resume file for the requested kind; 404 when the kind is unrecognized or the saved file does not exist. Reads the saved settings, never any unsaved form input.</summary>
    public static async Task<IResult> StreamResumeAsync(string kind, SettingsService settingsService, CancellationToken cancellationToken)
    {
        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        ResumeFileSelection? selection = ResumeFileSelector.Select(settings, kind);

        if (selection is null || selection.Path.Length == 0 || !File.Exists(selection.Path))
        {
            return Results.NotFound();
        }

        return new InlineFileResult(selection.Path, selection.ContentType);
    }

    /// <summary>Streams a file from disk with an inline content disposition, so the browser renders it in the tab instead of downloading it.</summary>
    private sealed class InlineFileResult(string path, string contentType) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = contentType;
            httpContext.Response.Headers.ContentDisposition = $"inline; filename=\"{Path.GetFileName(path)}\"";

            await using FileStream stream = File.OpenRead(path);
            httpContext.Response.ContentLength = stream.Length;
            await stream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
        }
    }
}
