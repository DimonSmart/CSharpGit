using System.Text.Json;
using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

public sealed class JsonAppSettingsServiceTests
{
    [Fact]
    public void MissingSettingsFileUsesSystemTheme()
    {
        using var fixture = new SettingsFixture();

        var service = fixture.CreateService();

        Assert.Equal(ApplicationThemeMode.System, service.ThemeMode);
    }

    [Theory]
    [InlineData(ApplicationThemeMode.System, "System")]
    [InlineData(ApplicationThemeMode.Light, "Light")]
    [InlineData(ApplicationThemeMode.Dark, "Dark")]
    public async Task ThemeModeIsPersistedAsReadableString(ApplicationThemeMode mode, string expected)
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();

        if (mode == ApplicationThemeMode.System)
            await service.SetThemeModeAsync(ApplicationThemeMode.Light);

        await service.SetThemeModeAsync(mode);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SettingsPath));
        Assert.Equal(expected, document.RootElement.GetProperty("ThemeMode").GetString());
    }

    [Theory]
    [InlineData(ApplicationThemeMode.System)]
    [InlineData(ApplicationThemeMode.Light)]
    [InlineData(ApplicationThemeMode.Dark)]
    public async Task NewServiceRestoresPersistedThemeMode(ApplicationThemeMode mode)
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();

        if (mode == ApplicationThemeMode.System)
            await service.SetThemeModeAsync(ApplicationThemeMode.Light);
        await service.SetThemeModeAsync(mode);

        var restored = fixture.CreateService();

        Assert.Equal(mode, restored.ThemeMode);
    }

    [Fact]
    public void OldSettingsWithoutThemeModeUseSystemAndKeepOtherSettings()
    {
        using var fixture = new SettingsFixture();
        fixture.WriteSettings(
            """
            {
              "CommitTimeDisplayMode": "Absolute",
              "LoggingEnabled": true,
              "LogLevel": "Debug"
            }
            """);

        var service = fixture.CreateService();

        Assert.Equal(ApplicationThemeMode.System, service.ThemeMode);
        Assert.Equal(CommitTimeDisplayMode.Absolute, service.CommitTimeDisplayMode);
        Assert.True(service.LoggingEnabled);
        Assert.Equal(ApplicationLogLevel.Debug, service.LogLevel);
    }

    [Fact]
    public void UnknownThemeModeUsesSystemAndKeepsOtherSettings()
    {
        using var fixture = new SettingsFixture();
        fixture.WriteSettings(
            """
            {
              "CommitTimeDisplayMode": "Absolute",
              "LoggingEnabled": true,
              "LogLevel": "Debug",
              "ThemeMode": "SomethingUnknown"
            }
            """);

        var service = fixture.CreateService();

        Assert.Equal(ApplicationThemeMode.System, service.ThemeMode);
        Assert.Equal(CommitTimeDisplayMode.Absolute, service.CommitTimeDisplayMode);
        Assert.True(service.LoggingEnabled);
        Assert.Equal(ApplicationLogLevel.Debug, service.LogLevel);
    }

    [Fact]
    public void MalformedThemeValueUsesSystemAndKeepsOtherSettings()
    {
        using var fixture = new SettingsFixture();
        fixture.WriteSettings(
            """
            {
              "CommitTimeDisplayMode": "Relative",
              "LoggingEnabled": true,
              "LogLevel": "Warning",
              "ThemeMode": { "unexpected": true }
            }
            """);

        var service = fixture.CreateService();

        Assert.Equal(ApplicationThemeMode.System, service.ThemeMode);
        Assert.Equal(CommitTimeDisplayMode.Relative, service.CommitTimeDisplayMode);
        Assert.True(service.LoggingEnabled);
        Assert.Equal(ApplicationLogLevel.Warning, service.LogLevel);
    }

    [Fact]
    public async Task InvalidThemeModeThroughApiThrowsArgumentOutOfRangeException()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SetThemeModeAsync((ApplicationThemeMode)123));

        Assert.Equal(ApplicationThemeMode.System, service.ThemeMode);
        Assert.False(File.Exists(fixture.SettingsPath));
    }

    [Fact]
    public async Task ThemeChangeRaisesChangedExactlyOnce()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        await service.SetThemeModeAsync(ApplicationThemeMode.Dark);

        Assert.Equal(1, changes);
        Assert.Equal(ApplicationThemeMode.Dark, service.ThemeMode);
    }

    [Fact]
    public async Task ReapplyingSameThemeDoesNotRaiseChanged()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        await service.SetThemeModeAsync(ApplicationThemeMode.Dark);
        var changes = 0;
        service.Changed += (_, _) => changes++;

        await service.SetThemeModeAsync(ApplicationThemeMode.Dark);

        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task PersistenceFailureKeepsOptimisticThemeAndPropagatesError()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateServiceWithBlockedParent();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        var exception = await Record.ExceptionAsync(
            () => service.SetThemeModeAsync(ApplicationThemeMode.Dark));

        Assert.NotNull(exception);
        Assert.Equal(ApplicationThemeMode.Dark, service.ThemeMode);
        Assert.Equal(1, changes);
    }

    private sealed class SettingsFixture : IDisposable
    {
        public SettingsFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "csharpgit-settings-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            SettingsPath = Path.Combine(DirectoryPath, "settings.json");
        }

        private string DirectoryPath { get; }

        public string SettingsPath { get; }

        public JsonAppSettingsService CreateService() => new(SettingsPath);

        public JsonAppSettingsService CreateServiceWithBlockedParent()
        {
            var blockedParent = Path.Combine(DirectoryPath, "blocked-parent");
            File.WriteAllText(blockedParent, "not a directory");
            return new JsonAppSettingsService(Path.Combine(blockedParent, "settings.json"));
        }

        public void WriteSettings(string json) => File.WriteAllText(SettingsPath, json);

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
    }
}
