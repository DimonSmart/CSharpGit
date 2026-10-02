using System.Text.Json;
using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

public sealed class DefaultRepositoriesDirectorySettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"csharpgit-settings-clone-{Guid.NewGuid():N}");

    public DefaultRepositoriesDirectorySettingsTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void MissingSettingUsesPlatformDefault()
    {
        var service = new JsonAppSettingsService(Path.Combine(_root, "missing.json"));

        Assert.True(Path.IsPathFullyQualified(service.DefaultRepositoriesDirectory));
        Assert.Equal(
            Path.Combine("source", "repos"),
            Path.GetRelativePath(
                Directory.GetParent(Directory.GetParent(service.DefaultRepositoriesDirectory)!.FullName)!.FullName,
                service.DefaultRepositoriesDirectory));
    }

    [Fact]
    public async Task PersistsAbsoluteDirectoryWithoutCreatingIt()
    {
        var settingsPath = Path.Combine(_root, "settings.json");
        var target = Path.Combine(_root, "not-created", "repos");
        var service = new JsonAppSettingsService(settingsPath);

        await service.SetDefaultRepositoriesDirectoryAsync($"  {target}  ");

        Assert.False(Directory.Exists(target));
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)),
            service.DefaultRepositoriesDirectory);

        var reloaded = new JsonAppSettingsService(settingsPath);
        Assert.Equal(service.DefaultRepositoriesDirectory, reloaded.DefaultRepositoriesDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/repos")]
    public async Task RejectsInvalidConfiguredDirectory(string value)
    {
        var service = new JsonAppSettingsService(Path.Combine(_root, "settings.json"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SetDefaultRepositoriesDirectoryAsync(value));
    }

    [Fact]
    public async Task InvalidPersistedDirectoryFallsBackToPlatformDefault()
    {
        var settingsPath = Path.Combine(_root, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            JsonSerializer.Serialize(new
            {
                DefaultRepositoriesDirectory = "relative/repos"
            }));

        var service = new JsonAppSettingsService(settingsPath);

        Assert.True(Path.IsPathFullyQualified(service.DefaultRepositoriesDirectory));
        Assert.EndsWith(
            Path.Combine("source", "repos"),
            service.DefaultRepositoriesDirectory,
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
