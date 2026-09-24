using System.Text;
using JobHunter.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Settings;

/// <summary>Covers <see cref="ResumeFileSelector"/>'s kind-to-path selection: the only input the resume endpoint takes from the caller.</summary>
/// <remarks>Settings is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
public sealed class ResumeFileSelectorTests
{
    [Fact]
    public void Select_PdfKind_ReturnsTheSavedPdfPathWithPdfContentType()
    {
        JobHunter.Domain.Settings settings = SeedSettings(@"C:\resume\cv.pdf", @"C:\resume\cv.md");

        ResumeFileSelection? selection = ResumeFileSelector.Select(settings, ResumeFileSelector.PdfKind);

        Assert.NotNull(selection);
        Assert.Equal(@"C:\resume\cv.pdf", selection.Path);
        Assert.Equal("application/pdf", selection.ContentType);
    }

    [Fact]
    public void Select_MarkdownKind_ReturnsTheSavedMarkdownPathWithTextContentType()
    {
        JobHunter.Domain.Settings settings = SeedSettings(@"C:\resume\cv.pdf", @"C:\resume\cv.md");

        ResumeFileSelection? selection = ResumeFileSelector.Select(settings, ResumeFileSelector.MarkdownKind);

        Assert.NotNull(selection);
        Assert.Equal(@"C:\resume\cv.md", selection.Path);
        Assert.Equal("text/plain; charset=utf-8", selection.ContentType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("docx")]
    [InlineData("../secrets")]
    [InlineData("PDF")]
    public void Select_AnyKindOtherThanPdfOrMarkdown_ReturnsNull(string kind)
    {
        JobHunter.Domain.Settings settings = SeedSettings(@"C:\resume\cv.pdf", @"C:\resume\cv.md");

        ResumeFileSelection? selection = ResumeFileSelector.Select(settings, kind);

        Assert.Null(selection);
    }

    private static JobHunter.Domain.Settings SeedSettings(string resumePdfPath, string resumeMarkdownPath)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureResume(resumePdfPath, resumeMarkdownPath);

        return settings;
    }
}

/// <summary>Covers <see cref="SettingsEndpoints.StreamResumeAsync"/> end to end against a real settings row and real files on disk: the saved path is read, never a caller-supplied one, and a missing file or an unrecognized kind both 404.</summary>
public sealed class SettingsEndpointsTests : IAsyncLifetime
{
    private static readonly IServiceProvider EmptyServiceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();

    private readonly SettingsEndpointsTestHarness harness = new();
    private readonly string tempFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(tempFolder);

        return harness.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await harness.DisposeAsync();

        if (Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public async Task StreamResumeAsync_PdfKindWithAnExistingSavedFile_StreamsItInlineAsApplicationPdf()
    {
        string pdfPath = Path.Combine(tempFolder, "resume.pdf");
        byte[] pdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34];
        await File.WriteAllBytesAsync(pdfPath, pdfBytes);
        await harness.Settings.ApplyAsync(settings => settings.ConfigureResume(pdfPath, Path.Combine(tempFolder, "missing.md")));

        DefaultHttpContext httpContext = await ExecuteAsync(ResumeFileSelector.PdfKind);

        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal("application/pdf", httpContext.Response.ContentType);
        Assert.Contains("inline", httpContext.Response.Headers.ContentDisposition.ToString());
        Assert.Equal(pdfBytes, ReadBody(httpContext));
    }

    [Fact]
    public async Task StreamResumeAsync_MarkdownKindWithAnExistingSavedFile_StreamsItAsTextPlainUtf8()
    {
        string markdownPath = Path.Combine(tempFolder, "resume.md");
        const string markdownText = "# Resume\n\nSummary.";
        await File.WriteAllTextAsync(markdownPath, markdownText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await harness.Settings.ApplyAsync(settings => settings.ConfigureResume(Path.Combine(tempFolder, "missing.pdf"), markdownPath));

        DefaultHttpContext httpContext = await ExecuteAsync(ResumeFileSelector.MarkdownKind);

        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", httpContext.Response.ContentType);
        Assert.Equal(markdownText, Encoding.UTF8.GetString(ReadBody(httpContext)));
    }

    [Fact]
    public async Task StreamResumeAsync_KindNotPdfOrMarkdown_ReturnsNotFound()
    {
        await harness.Settings.ApplyAsync(settings => settings.ConfigureResume(Path.Combine(tempFolder, "resume.pdf"), Path.Combine(tempFolder, "resume.md")));

        DefaultHttpContext httpContext = await ExecuteAsync("docx");

        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task StreamResumeAsync_SavedPathDoesNotExistOnDisk_ReturnsNotFound()
    {
        await harness.Settings.ApplyAsync(settings => settings.ConfigureResume(Path.Combine(tempFolder, "nowhere.pdf"), Path.Combine(tempFolder, "resume.md")));

        DefaultHttpContext httpContext = await ExecuteAsync(ResumeFileSelector.PdfKind);

        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task StreamResumeAsync_ReadsTheSavedSettingsRowNotSomeOtherPath_EvenWhenCalledRepeatedly()
    {
        string firstPdfPath = Path.Combine(tempFolder, "first.pdf");
        string secondPdfPath = Path.Combine(tempFolder, "second.pdf");
        await File.WriteAllBytesAsync(firstPdfPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(secondPdfPath, [4, 5, 6, 7]);
        await harness.Settings.ApplyAsync(settings => settings.ConfigureResume(firstPdfPath, Path.Combine(tempFolder, "missing.md")));

        DefaultHttpContext beforeResave = await ExecuteAsync(ResumeFileSelector.PdfKind);
        Assert.Equal([1, 2, 3], ReadBody(beforeResave));

        await harness.Settings.ApplyAsync(settings => settings.ConfigureResume(secondPdfPath, Path.Combine(tempFolder, "missing.md")));
        DefaultHttpContext afterResave = await ExecuteAsync(ResumeFileSelector.PdfKind);
        Assert.Equal([4, 5, 6, 7], ReadBody(afterResave));
    }

    private async Task<DefaultHttpContext> ExecuteAsync(string kind)
    {
        DefaultHttpContext httpContext = new()
        {
            RequestServices = EmptyServiceProvider
        };
        httpContext.Response.Body = new MemoryStream();

        IResult result = await SettingsEndpoints.StreamResumeAsync(kind, harness.Settings, CancellationToken.None);
        await result.ExecuteAsync(httpContext);

        return httpContext;
    }

    private static byte[] ReadBody(DefaultHttpContext httpContext)
    {
        MemoryStream body = (MemoryStream)httpContext.Response.Body;

        return body.ToArray();
    }
}
