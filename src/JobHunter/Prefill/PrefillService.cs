using JobHunter.Data;
using JobHunter.Settings;
using Microsoft.Playwright;

namespace JobHunter.Prefill;

/// <summary>Opens an application form in a headed browser, types the standard fields and attaches the resume, then leaves the window open for the custom questions and the submit, which stay a human decision.</summary>
/// <remarks>The browser binary is a one-time install: from <c>src/JobHunter/bin/Debug/net10.0/</c> run <c>./playwright.ps1 install chromium</c> once after the first build; with an endpoint configured prefill attaches to a browser already running on the desktop instead and needs no binary of its own.</remarks>
/// <remarks>No control that submits an application is ever clicked, pressed or invoked here: the service only navigates, types and uploads.</remarks>
/// <remarks>A board that renders its markup on the server and wires it up in the browser afterwards drops anything set before that wiring runs, so the form is given a moment to settle before the first field is touched.</remarks>
public sealed class PrefillService(SettingsService settingsReader, DataPaths paths, PrefillBrowserSource browserSource, ILogger<PrefillService> logger) : IAsyncDisposable
{
    private const float FormTimeoutMilliseconds = 20000;
    private const float FieldTimeoutMilliseconds = 4000;
    private const float ResumeSettleMilliseconds = 1500;
    private const float HydrationSettleMilliseconds = 1200;

    private static readonly IReadOnlyList<PrefillField> FillOrder =
    [
        PrefillField.Resume,
        PrefillField.FullName,
        PrefillField.FirstName,
        PrefillField.LastName,
        PrefillField.Email,
        PrefillField.Phone,
        PrefillField.Location,
        PrefillField.LinkedIn,
        PrefillField.CoverLetter
    ];

    private readonly SemaphoreSlim gate = new(1, 1);
    private IPlaywright? driver;
    private IBrowserContext? browser;

    /// <summary>Opens the apply target and fills it when its host is one of the mapped systems; any other host is opened and left untouched.</summary>
    public async Task<PrefillOutcome> RunAsync(string applyUrl, string? coverLetter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applyUrl);

        AtsFormMap? map = AtsFormMap.ForUrl(applyUrl);
        Domain.Settings settings = await settingsReader.GetAsync(cancellationToken);
        PrefillValues values = PrefillValues.From(settings, coverLetter);

        await gate.WaitAsync(cancellationToken);

        try
        {
            IBrowserContext context = await OpenBrowserAsync(cancellationToken);
            IPage page = context.Pages.Count > 0 && context.Pages[0].Url is "about:blank" ? context.Pages[0] : await context.NewPageAsync();

            await page.GotoAsync(applyUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = FormTimeoutMilliseconds });
            await page.BringToFrontAsync();

            if (map is null)
            {
                logger.LogInformation("Prefill opened {ApplyUrl} without a form map.", applyUrl);

                return PrefillOutcome.OpenedUrlOnly(applyUrl);
            }

            return await FillAsync(page, map, values, applyUrl);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Prefill of {ApplyUrl} failed.", applyUrl);

            return PrefillOutcome.Failed(applyUrl, error.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Closes the browser it started when the application stops; a browser that was already running on the desktop is only let go of, and a prefill on its own never closes either.</summary>
    public async ValueTask DisposeAsync()
    {
        if (browser is not null && browserSource.ClosesOnShutdown)
        {
            await browser.CloseAsync();
        }

        browser = null;
        driver?.Dispose();
        driver = null;
        gate.Dispose();
    }

    private async Task<PrefillOutcome> FillAsync(IPage page, AtsFormMap map, PrefillValues values, string applyUrl)
    {
        ILocator anchor = page.Locator(map.ReadySelector).First;

        try
        {
            await anchor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = FormTimeoutMilliseconds });
            await page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions { Timeout = FormTimeoutMilliseconds });
        }
        catch (PlaywrightException)
        {
            return PrefillOutcome.Failed(applyUrl, $"the {map.Ats} form did not appear, so its selector map needs a look.");
        }

        await page.WaitForTimeoutAsync(HydrationSettleMilliseconds);

        List<PrefillField> filled = [];
        List<PrefillField> missing = [];

        foreach (PrefillField field in FillOrder)
        {
            string? value = values.ValueFor(field);
            IReadOnlyList<string> selectors = map.SelectorsFor(field);

            if (value is null || selectors.Count == 0)
            {
                continue;
            }

            if (await TypeAsync(page, field, selectors, value))
            {
                filled.Add(field);
            }
            else
            {
                missing.Add(field);
            }
        }

        await anchor.ScrollIntoViewIfNeededAsync();
        logger.LogInformation("Prefill filled {Filled} on the {Ats} form at {ApplyUrl}.", filled.Count, map.Ats, applyUrl);

        return PrefillOutcome.Fields(map.Ats, applyUrl, filled, missing);
    }

    private async Task<bool> TypeAsync(IPage page, PrefillField field, IReadOnlyList<string> selectors, string value)
    {
        ILocator? target = await FindAsync(page, selectors);

        if (target is null)
        {
            return false;
        }

        try
        {
            if (field == PrefillField.Resume)
            {
                if (!File.Exists(value))
                {
                    logger.LogWarning("The resume at {ResumePath} does not exist, so nothing was attached.", value);

                    return false;
                }

                await target.SetInputFilesAsync(value, new LocatorSetInputFilesOptions { Timeout = FieldTimeoutMilliseconds });
                await page.WaitForTimeoutAsync(ResumeSettleMilliseconds);

                return true;
            }

            await target.FillAsync(value, new LocatorFillOptions { Timeout = FieldTimeoutMilliseconds });

            return true;
        }
        catch (PlaywrightException error)
        {
            logger.LogWarning(error, "The {Field} control was found but refused the value.", field);

            return false;
        }
    }

    private static async Task<ILocator?> FindAsync(IPage page, IReadOnlyList<string> selectors)
    {
        foreach (string selector in selectors)
        {
            ILocator locator = page.Locator(selector).First;

            if (await locator.CountAsync() > 0)
            {
                return locator;
            }
        }

        return null;
    }

    private async Task<IBrowserContext> OpenBrowserAsync(CancellationToken cancellationToken)
    {
        if (browser is not null)
        {
            return browser;
        }

        driver ??= await Playwright.CreateAsync();
        browser = browserSource.Mode is PrefillBrowserMode.Connect ? await AttachAsync(cancellationToken) : await StartAsync();
        browser.Close += (_, _) => browser = null;

        return browser;
    }

    private async Task<IBrowserContext> StartAsync()
    {
        logger.LogInformation("Prefill is starting its own browser with the profile at {Profile}.", paths.Browser);

        return await driver!.Chromium.LaunchPersistentContextAsync(paths.Browser, new BrowserTypeLaunchPersistentContextOptions { Headless = false });
    }

    private async Task<IBrowserContext> AttachAsync(CancellationToken cancellationToken)
    {
        string endpoint = await browserSource.ResolveEndpointAsync(cancellationToken);
        logger.LogInformation("Prefill is attaching to the browser on the desktop at {Endpoint}.", endpoint);

        IBrowser desktop = await driver!.Chromium.ConnectOverCDPAsync(endpoint);

        return desktop.Contexts.Count > 0 ? desktop.Contexts[0] : await desktop.NewContextAsync();
    }
}
