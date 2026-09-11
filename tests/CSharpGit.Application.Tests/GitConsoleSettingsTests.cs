using System.Text.Json;
using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

public sealed class GitConsoleSettingsTests
{
    [Fact]
    public void MissingSettingsUseOnErrorsByDefault()
    {
        using var fixture = new SettingsFixture();

        var service = fixture.CreateService();

        Assert.Equal(GitConsoleAutoOpenMode.OnErrors, service.GitConsoleAutoOpenMode);
    }

    [Theory]
    [InlineData(GitConsoleAutoOpenMode.OnErrors, "OnErrors")]
    [InlineData(GitConsoleAutoOpenMode.Always, "Always")]
    [InlineData(GitConsoleAutoOpenMode.Never, "Never")]
    public async Task AutoOpenModeIsPersistedAndRestored(GitConsoleAutoOpenMode mode, string expectedJson)
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();

        if (mode == GitConsoleAutoOpenMode.OnErrors)
            await service.SetGitConsoleAutoOpenModeAsync(GitConsoleAutoOpenMode.Always);
        await service.SetGitConsoleAutoOpenModeAsync(mode);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SettingsPath));
        Assert.Equal(expectedJson, document.RootElement.GetProperty("GitConsoleAutoOpenMode").GetString());
        Assert.Equal(mode, fixture.CreateService().GitConsoleAutoOpenMode);
    }

    [Fact]
    public void OldSettingsWithoutGitConsoleModeUseOnErrors()
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(
            fixture.SettingsPath,
            """
            {
              "ThemeMode": "Dark",
              "CommitTimeDisplayMode": "Relative"
            }
            """);

        var service = fixture.CreateService();

        Assert.Equal(GitConsoleAutoOpenMode.OnErrors, service.GitConsoleAutoOpenMode);
        Assert.Equal(ApplicationThemeMode.Dark, service.ThemeMode);
        Assert.Equal(CommitTimeDisplayMode.Relative, service.CommitTimeDisplayMode);
    }

    private sealed class SettingsFixture : IDisposable
    {
        private readonly string _directory;

        public SettingsFixture()
        {
            _directory = Path.Combine(Path.GetTempPath(), "csharpgit-git-console-settings-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SettingsPath = Path.Combine(_directory, "settings.json");
        }

        public string SettingsPath { get; }

        public JsonAppSettingsService CreateService() => new(SettingsPath);

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }
}
