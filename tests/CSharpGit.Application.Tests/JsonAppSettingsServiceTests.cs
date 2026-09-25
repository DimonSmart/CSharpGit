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
    public async Task PersistenceFailureKeepsCommittedStateAndDoesNotPublishChange()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateServiceWithBlockedParent();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        var exception = await Record.ExceptionAsync(
            () => service.SetThemeModeAsync(ApplicationThemeMode.Dark));

        Assert.NotNull(exception);
        Assert.Equal(ApplicationThemeMode.System, service.ThemeMode);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task ConcurrentUpdatesMergeAgainstLatestCommittedSnapshot()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        var changes = 0;
        service.Changed += (_, _) => Interlocked.Increment(ref changes);

        await Task.WhenAll(
            service.SetThemeModeAsync(ApplicationThemeMode.Dark),
            service.SetCommitTimeDisplayModeAsync(CommitTimeDisplayMode.Absolute),
            service.SetAutoSetupRemoteOnPushAsync(true));

        Assert.Equal(ApplicationThemeMode.Dark, service.ThemeMode);
        Assert.Equal(CommitTimeDisplayMode.Absolute, service.CommitTimeDisplayMode);
        Assert.True(service.AutoSetupRemoteOnPush);
        Assert.Equal(3, changes);

        var restored = fixture.CreateService();
        Assert.Equal(ApplicationThemeMode.Dark, restored.ThemeMode);
        Assert.Equal(CommitTimeDisplayMode.Absolute, restored.CommitTimeDisplayMode);
        Assert.True(restored.AutoSetupRemoteOnPush);
    }

    [Fact]
    public async Task FailedPreparationKeepsPreviousSettingsFileAndCommittedState()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        await service.SetThemeModeAsync(ApplicationThemeMode.Light);
        var previousFile = await File.ReadAllTextAsync(fixture.SettingsPath);
        Directory.CreateDirectory(fixture.SettingsPath + ".tmp");
        var changes = 0;
        service.Changed += (_, _) => changes++;

        var exception = await Record.ExceptionAsync(
            () => service.SetThemeModeAsync(ApplicationThemeMode.Dark));

        Assert.NotNull(exception);
        Assert.Equal(ApplicationThemeMode.Light, service.ThemeMode);
        Assert.Equal(previousFile, await File.ReadAllTextAsync(fixture.SettingsPath));
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task FailedMoveCleansTemporaryFileAndDoesNotPublishCandidateState()
    {
        using var fixture = new SettingsFixture();
        Directory.CreateDirectory(fixture.SettingsPath);
        var service = fixture.CreateService();
        var temporaryPath = fixture.SettingsPath + ".tmp";
        var changes = 0;
        service.Changed += (_, _) => changes++;

        var exception = await Record.ExceptionAsync(
            () => service.SetThemeModeAsync(ApplicationThemeMode.Dark));

        Assert.NotNull(exception);
        Assert.Equal(ApplicationThemeMode.System, service.ThemeMode);
        Assert.False(File.Exists(temporaryPath));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void AutoSetupRemoteOnPushDefaultsToFalse()
    {
        using var fixture = new SettingsFixture();

        var service = fixture.CreateService();

        Assert.False(service.AutoSetupRemoteOnPush);
    }

    [Fact]
    public async Task AutoSetupRemoteOnPushIsPersisted()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();

        await service.SetAutoSetupRemoteOnPushAsync(true);
        var restored = fixture.CreateService();

        Assert.True(service.AutoSetupRemoteOnPush);
        Assert.True(restored.AutoSetupRemoteOnPush);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SettingsPath));
        Assert.True(document.RootElement.GetProperty("AutoSetupRemoteOnPush").GetBoolean());
    }

    [Fact]
    public void AuthorAvatarSettingsDefaultToEnabledForMissingAndLegacySettings()
    {
        using var fixture = new SettingsFixture();
        var missing = fixture.CreateService();
        Assert.True(missing.ShowAuthorAvatars);
        Assert.True(missing.OnlineAvatarLookupEnabled);

        fixture.WriteSettings("{ \"ThemeMode\": \"Dark\" }");
        var legacy = fixture.CreateService();
        Assert.True(legacy.ShowAuthorAvatars);
        Assert.True(legacy.OnlineAvatarLookupEnabled);
    }

    [Fact]
    public async Task AuthorAvatarSettingsArePersistedAndRestored()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();

        await service.SetShowAuthorAvatarsAsync(false);
        await service.SetOnlineAvatarLookupEnabledAsync(false);

        var restored = fixture.CreateService();
        Assert.False(restored.ShowAuthorAvatars);
        Assert.False(restored.OnlineAvatarLookupEnabled);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SettingsPath));
        Assert.False(document.RootElement.GetProperty("ShowAuthorAvatars").GetBoolean());
        Assert.False(document.RootElement.GetProperty("OnlineAvatarLookupEnabled").GetBoolean());
    }

    [Fact]
    public void HistoryPerformanceDiagnosticsDefaultsToFalseForMissingAndLegacySettings()
    {
        using var fixture = new SettingsFixture();
        Assert.False(fixture.CreateService().HistoryPerformanceDiagnosticsEnabled);

        fixture.WriteSettings("{ \"ThemeMode\": \"Dark\", \"LoggingEnabled\": true }");
        Assert.False(fixture.CreateService().HistoryPerformanceDiagnosticsEnabled);
    }

    [Fact]
    public async Task HistoryPerformanceDiagnosticsIsPersistedAndRestored()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        await service.SetHistoryPerformanceDiagnosticsEnabledAsync(true);

        Assert.True(service.HistoryPerformanceDiagnosticsEnabled);
        Assert.Equal(1, changes);
        var restored = fixture.CreateService();
        Assert.True(restored.HistoryPerformanceDiagnosticsEnabled);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SettingsPath));
        Assert.True(document.RootElement.GetProperty("HistoryPerformanceDiagnosticsEnabled").GetBoolean());
    }

    [Fact]
    public void HistoryRenderingModeDefaultsToFullForMissingAndLegacySettings()
    {
        using var fixture = new SettingsFixture();
        Assert.Equal(HistoryRenderingMode.Full, fixture.CreateService().HistoryRenderingMode);

        fixture.WriteSettings("{ \"ThemeMode\": \"Dark\", \"HistoryPerformanceDiagnosticsEnabled\": true }");
        Assert.Equal(HistoryRenderingMode.Full, fixture.CreateService().HistoryRenderingMode);
    }

    [Fact]
    public void LegacySimplifiedHistoryRenderingMigratesToSubjectOnly()
    {
        using var fixture = new SettingsFixture();
        fixture.WriteSettings("{ \"HistorySimplifiedRenderingEnabled\": true }");

        Assert.Equal(HistoryRenderingMode.SubjectOnly, fixture.CreateService().HistoryRenderingMode);
    }

    [Theory]
    [InlineData(HistoryRenderingMode.SubjectOnly, "SubjectOnly")]
    [InlineData(HistoryRenderingMode.TextColumns, "TextColumns")]
    [InlineData(HistoryRenderingMode.TextAndGraph, "TextAndGraph")]
    [InlineData(HistoryRenderingMode.TextGraphAndReferences, "TextGraphAndReferences")]
    [InlineData(HistoryRenderingMode.Full, "Full")]
    public async Task HistoryRenderingModeIsPersistedAndRestored(
        HistoryRenderingMode mode,
        string expectedJsonValue)
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();

        if (mode == HistoryRenderingMode.Full)
            await service.SetHistoryRenderingModeAsync(HistoryRenderingMode.SubjectOnly);
        await service.SetHistoryRenderingModeAsync(mode);

        Assert.Equal(mode, service.HistoryRenderingMode);
        var restored = fixture.CreateService();
        Assert.Equal(mode, restored.HistoryRenderingMode);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SettingsPath));
        Assert.Equal(expectedJsonValue, document.RootElement.GetProperty("HistoryRenderingMode").GetString());
        Assert.False(document.RootElement.TryGetProperty("HistorySimplifiedRenderingEnabled", out _));
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
