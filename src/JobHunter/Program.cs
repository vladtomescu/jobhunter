using JobHunter.Applications;
using JobHunter.Components;
using JobHunter.Data;
using JobHunter.Jobs;
using JobHunter.Llm;
using JobHunter.Pipeline;
using JobHunter.Prefill;
using JobHunter.Refresh;
using JobHunter.Settings;
using JobHunter.Sources;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(ApiKeyDetector.LocalSettingsFile, optional: true, reloadOnChange: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddData()
    .AddSettings()
    .AddSources()
    .AddPipeline()
    .AddLlm()
    .AddApplications()
    .AddJobs()
    .AddRefresh()
    .AddPrefill();

WebApplication app = builder.Build();

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
await app.Services.GetRequiredService<HighlightFlagBackfill>().RunAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

if (app.Configuration.GetSection("Kestrel:Endpoints:Https").Exists())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapSettingsEndpoints();

await app.RunAsync();
