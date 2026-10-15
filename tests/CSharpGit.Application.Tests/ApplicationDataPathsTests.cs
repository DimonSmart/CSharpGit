using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

[CollectionDefinition("Application data environment", DisableParallelization = true)]
public sealed class ApplicationDataEnvironmentCollection;

[Collection("Application data environment")]
public sealed class ApplicationDataPathsTests
{
    [Fact]
    public void ExplicitRootOverridesTheOperatingSystemKnownFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "csharpgit-data-tests", Guid.NewGuid().ToString("N"));
        WithRoot(root, () => Assert.Equal(Path.GetFullPath(root), ApplicationDataPaths.Root));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void UnconfiguredRootPreservesTheDefaultLocation(string? configured)
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(expected)) expected = AppContext.BaseDirectory;
        WithRoot(configured, () => Assert.Equal(expected, ApplicationDataPaths.Root));
    }

    [Fact]
    public async Task DefaultSettingsConstructorReadsAndWritesOnlyTheExplicitRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "csharpgit-data-tests", Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable(ApplicationDataPaths.RootEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ApplicationDataPaths.RootEnvironmentVariable, root);
            var settings = new JsonAppSettingsService();
            Assert.False(settings.LoggingEnabled);
            await settings.SetLoggingSettingsAsync(true, settings.LogLevel);
            Assert.True(File.Exists(Path.Combine(root, "CSharpGit.Presentation", "settings.json")));
            Assert.True(new JsonAppSettingsService().LoggingEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ApplicationDataPaths.RootEnvironmentVariable, previous);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void WithRoot(string? root, Action assertion)
    {
        var previous = Environment.GetEnvironmentVariable(ApplicationDataPaths.RootEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ApplicationDataPaths.RootEnvironmentVariable, root);
            assertion();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ApplicationDataPaths.RootEnvironmentVariable, previous);
        }
    }
}
