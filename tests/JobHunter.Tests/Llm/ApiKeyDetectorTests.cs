using JobHunter.Llm;
using Microsoft.Extensions.Configuration;

namespace JobHunter.Tests.Llm;

/// <summary>Proves where the detector looks for the key and in which order.</summary>
public sealed class ApiKeyDetectorTests
{
    [Fact]
    public void Read_WithTheConfigurationKeySet_ReturnsItBeforeTheEnvironmentVariable()
    {
        ApiKeyDetector detector = Detector((ApiKeyDetector.ConfigurationKey, "from-file"), (ApiKeyDetector.VariableName, "from-environment"));

        Assert.Equal("from-file", detector.Read());
        Assert.True(detector.IsPresent);
    }

    [Fact]
    public void Read_WithOnlyTheEnvironmentVariableSet_FallsBackToIt()
    {
        ApiKeyDetector detector = Detector((ApiKeyDetector.VariableName, "from-environment"));

        Assert.Equal("from-environment", detector.Read());
    }

    [Fact]
    public void Read_WithBlankValuesEverywhere_ReturnsNull()
    {
        ApiKeyDetector detector = Detector((ApiKeyDetector.ConfigurationKey, "  "), (ApiKeyDetector.VariableName, ""));

        Assert.Null(detector.Read());
        Assert.False(detector.IsPresent);
    }

    private static ApiKeyDetector Detector(params (string Key, string Value)[] entries)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build();

        return new ApiKeyDetector(configuration);
    }
}
