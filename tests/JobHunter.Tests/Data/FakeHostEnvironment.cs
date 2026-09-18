using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace JobHunter.Tests.Data;

/// <summary>A host environment whose content root is set directly, standing in for the real one the web host builds.</summary>
internal sealed class FakeHostEnvironment : IHostEnvironment
{
    public string ContentRootPath { get; set; } = string.Empty;

    public string EnvironmentName { get; set; } = "Development";

    public string ApplicationName { get; set; } = "JobHunter.Tests";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
