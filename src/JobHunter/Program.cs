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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
