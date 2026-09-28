using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using JobHunter.Applications;
using JobHunter.Domain;
using JobHunter.Tests.Llm;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Applications;

/// <summary>Proves that a cover letter reads the same as plain text and as a Word document that validates, and that the download endpoint serves it under a clean name.</summary>
public sealed class CoverLetterDocumentTests
{
    private static readonly IServiceProvider EmptyServiceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static readonly ApplicationCoverLetter StoredLetter = new(
        "en",
        "Dear Northwind Labs team,",
        ["I am writing about the Senior Backend Engineer role.", "I own the ledger service that settles 40 million transactions a day.", "I would welcome a conversation."],
        "Kind regards,",
        new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero),
        "claude-code",
        []);

    [Fact]
    public void ToPlainText_OfAComposedLetter_PutsTheHeaderTogetherABlankLineBetweenBlocksAndTheNameUnderTheClosing()
    {
        CoverLetterText letter = CoverLetterText.Compose(StoredLetter, ["**{name}**", "{email} · {phone}"], Settings());

        string[] expected =
        [
            "Test Placeholder",
            "test.placeholder@example.com · +1 555 0100",
            string.Empty,
            "Dear Northwind Labs team,",
            string.Empty,
            "I am writing about the Senior Backend Engineer role.",
            string.Empty,
            "I own the ledger service that settles 40 million transactions a day.",
            string.Empty,
            "I would welcome a conversation.",
            string.Empty,
            "Kind regards,",
            "Test Placeholder"
        ];

        Assert.Equal(string.Join(Environment.NewLine, expected), letter.ToPlainText());
    }

    [Fact]
    public void ToPlainText_WithoutANameOrAHeader_EndsWithTheClosing()
    {
        CoverLetterText letter = CoverLetterText.Compose(StoredLetter, [], Settings(firstName: string.Empty, lastName: string.Empty));

        Assert.StartsWith("Dear Northwind Labs team,", letter.ToPlainText(), StringComparison.Ordinal);
        Assert.EndsWith("Kind regards,", letter.ToPlainText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OfAComposedLetter_WritesADocumentTheValidatorAccepts()
    {
        byte[] document = CoverLetterDocument.Build(CoverLetterText.Compose(StoredLetter, ["**{name}**", "{location} · {email}"], Settings()));

        using WordprocessingDocument opened = WordprocessingDocument.Open(new MemoryStream(document), isEditable: false);
        Assert.Empty(new OpenXmlValidator().Validate(opened));
    }

    [Fact]
    public void Build_OfAComposedLetter_CarriesTheHeaderTheBodyAndTheNameInReadingOrderWithABlankLineBetweenBlocks()
    {
        byte[] document = CoverLetterDocument.Build(CoverLetterText.Compose(StoredLetter, ["**{name}**", "{location} · {email}"], Settings()));

        using WordprocessingDocument opened = WordprocessingDocument.Open(new MemoryStream(document), isEditable: false);
        string[] paragraphs = [.. opened.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Select(paragraph => paragraph.InnerText)];
        Assert.Equal<string>(
            [
                "Test Placeholder",
                "Utrecht, Netherlands · test.placeholder@example.com",
                string.Empty,
                "Dear Northwind Labs team,",
                string.Empty,
                "I am writing about the Senior Backend Engineer role.",
                string.Empty,
                "I own the ledger service that settles 40 million transactions a day.",
                string.Empty,
                "I would welcome a conversation.",
                string.Empty,
                "Kind regards,",
                "Test Placeholder"
            ],
            paragraphs);
    }

    [Fact]
    public void Build_OfAComposedLetter_SetsTheNameLineBoldAndLargerOnA4WithOneFontAtElevenPoints()
    {
        byte[] document = CoverLetterDocument.Build(CoverLetterText.Compose(StoredLetter, ["{name}", "{email}"], Settings()));

        using WordprocessingDocument opened = WordprocessingDocument.Open(new MemoryStream(document), isEditable: false);
        Body body = opened.MainDocumentPart!.Document!.Body!;
        RunProperties nameLine = body.Elements<Paragraph>().First().Descendants<RunProperties>().Single();
        Assert.NotNull(nameLine.GetFirstChild<Bold>());
        Assert.Equal(CoverLetterDocument.NameSize, nameLine.GetFirstChild<FontSize>()!.Val!.Value);
        Assert.Empty(body.Elements<Paragraph>().ElementAt(1).Descendants<Bold>());
        PageSize page = body.GetFirstChild<SectionProperties>()!.GetFirstChild<PageSize>()!;
        Assert.Equal(11906U, page.Width!.Value);
        Assert.Equal(16838U, page.Height!.Value);
        RunPropertiesBaseStyle defaults = opened.MainDocumentPart.StyleDefinitionsPart!.Styles!.Descendants<RunPropertiesBaseStyle>().Single();
        Assert.Equal(CoverLetterDocument.FontName, defaults.GetFirstChild<RunFonts>()!.Ascii!.Value);
        Assert.Equal("22", defaults.GetFirstChild<FontSize>()!.Val!.Value);
    }

    [Theory]
    [InlineData("Test Placeholder", "Northwind Labs", "Test_Placeholder_Cover_Letter_Northwind_Labs.docx")]
    [InlineData("Test Placeholder", "Fabrikam, Inc. / EU", "Test_Placeholder_Cover_Letter_Fabrikam_Inc_EU.docx")]
    [InlineData("", "Northwind Labs", "Cover_Letter_Northwind_Labs.docx")]
    [InlineData("  ", "Contoso:\"Data\"*", "Cover_Letter_Contoso_Data.docx")]
    [InlineData("Test Placeholder", "", "Test_Placeholder_Cover_Letter.docx")]
    public void FileName_FromTheNameAndTheCompany_KeepsOnlyLettersDigitsAndSingleUnderscores(string name, string company, string expected)
    {
        Assert.Equal(expected, CoverLetterDocument.FileName(name, company));
    }

    [Fact]
    public async Task DownloadDocumentAsync_ForAJobWithALetter_StreamsAValidDocxNamedAfterTheCandidateAndTheCompany()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings => settings.ConfigureContact("Test", "Placeholder", "test.placeholder@example.com", "+1 555 0100", "Utrecht, Netherlands", string.Empty));
        Job job = LlmTestJobs.NewPursuedJob("Northwind Labs");
        await harness.SaveAsync(job);
        Application application = Application.Create(job.Id, ApplicationStatus.Saved, LlmTestJobs.SeenAt, "Saved from the inbox.");
        application.AttachCoverLetter(StoredLetter);
        await harness.SaveAsync(application);

        DefaultHttpContext httpContext = await ExecuteAsync(await CoverLetterEndpoints.DownloadDocumentAsync(job.Id, harness.JobQueries, harness.Settings, LlmFixtures.ExampleCatalog(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal(CoverLetterDocument.ContentType, httpContext.Response.ContentType);
        Assert.Contains("attachment", httpContext.Response.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);
        Assert.Contains("Test_Placeholder_Cover_Letter_Northwind_Labs.docx", httpContext.Response.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);
        using WordprocessingDocument opened = WordprocessingDocument.Open(new MemoryStream(((MemoryStream)httpContext.Response.Body).ToArray()), isEditable: false);
        Assert.Empty(new OpenXmlValidator().Validate(opened));
        string text = opened.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("Test Placeholder", text, StringComparison.Ordinal);
        Assert.Contains("Utrecht, Netherlands · test.placeholder@example.com · +1 555 0100", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{linkedin}", text, StringComparison.Ordinal);
        Assert.Contains("I own the ledger service", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadDocumentAsync_ForASavedJobWithoutALetter_ReturnsNotFound()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);
        await harness.SaveAsync(Application.Create(job.Id, ApplicationStatus.Saved, LlmTestJobs.SeenAt, "Saved from the inbox."));

        DefaultHttpContext httpContext = await ExecuteAsync(await CoverLetterEndpoints.DownloadDocumentAsync(job.Id, harness.JobQueries, harness.Settings, LlmFixtures.ExampleCatalog(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    private static async Task<DefaultHttpContext> ExecuteAsync(IResult result)
    {
        DefaultHttpContext httpContext = new() { RequestServices = EmptyServiceProvider };
        httpContext.Response.Body = new MemoryStream();

        await result.ExecuteAsync(httpContext);

        return httpContext;
    }

    private static JobHunter.Domain.Settings Settings(string firstName = "Test", string lastName = "Placeholder")
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureContact(firstName, lastName, "test.placeholder@example.com", "+1 555 0100", "Utrecht, Netherlands", string.Empty);

        return settings;
    }
}
