using System.Text.Json;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Infrastructure.Tests;

public sealed class JsonAppSettingsMultiInstanceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "csharpgit-multi-settings-tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");
    private JsonAppSettingsService Open() => new(SettingsPath);

    [Fact]
    public async Task StaleInstanceMergesDifferentSettingsInsteadOfOverwritingNewerState()
    {
        var first = Open();
        var second = Open();

        await first.SetThemeModeAsync(ApplicationThemeMode.Dark);
        await second.SetCommitTimeDisplayModeAsync(CommitTimeDisplayMode.Absolute);

        var persisted = Open();
        Assert.Equal(ApplicationThemeMode.Dark, persisted.ThemeMode);
        Assert.Equal(CommitTimeDisplayMode.Absolute, persisted.CommitTimeDisplayMode);
        Assert.Equal(ApplicationThemeMode.Dark, second.ThemeMode);
    }

    [Fact]
    public async Task ConcurrentIndependentInstancesPreserveEveryRepositoryGroup()
    {
        var instances = Enumerable.Range(0, 12).Select(_ => Open()).ToArray();

        await Task.WhenAll(instances.Select((service, index) =>
            service.CreateRepositoryGroupAsync($"Group {index}")));

        var groups = Open().RepositoryGroups;
        Assert.Equal(13, groups.Count);
        for (var index = 0; index < instances.Length; index++)
            Assert.Contains(groups, group => group.Name == $"Group {index}");
    }

    [Fact]
    public async Task NoOpAfterExternalWriteRefreshesSnapshotWithoutRewritingFile()
    {
        var first = Open();
        var second = Open();
        var notifications = 0;
        second.Changed += (_, _) => notifications++;

        await first.SetThemeModeAsync(ApplicationThemeMode.Dark);
        var originalText = File.ReadAllText(SettingsPath);
        await second.SetThemeModeAsync(ApplicationThemeMode.Dark);

        Assert.Equal(1, notifications);
        Assert.Equal(ApplicationThemeMode.Dark, second.ThemeMode);
        Assert.Equal(originalText, File.ReadAllText(SettingsPath));
        await second.SetThemeModeAsync(ApplicationThemeMode.Dark);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task HeldFileLockCanBeCanceledWithoutChangingCommittedState()
    {
        var service = Open();
        Directory.CreateDirectory(_directory);
        await using var heldLock = new FileStream(
            SettingsPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SetThemeModeAsync(ApplicationThemeMode.Dark, cts.Token));

        Assert.Equal(ApplicationThemeMode.System, service.ThemeMode);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task CorruptExternalJsonIsNeverReplacedByAnUnrelatedWrite()
    {
        var service = Open();
        await service.SetThemeModeAsync(ApplicationThemeMode.Dark);
        const string corrupt = "{ bad settings json";
        File.WriteAllText(SettingsPath, corrupt);
        var notifications = 0;
        service.Changed += (_, _) => notifications++;

        await Assert.ThrowsAsync<JsonException>(() =>
            service.SetCommitTimeDisplayModeAsync(CommitTimeDisplayMode.Absolute));

        Assert.Equal(corrupt, File.ReadAllText(SettingsPath));
        Assert.Equal(ApplicationThemeMode.Dark, service.ThemeMode);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task TwoInstancesPreserveRecentRepositoriesAndGroups()
    {
        var first = Open();
        var second = Open();
        var firstPath = Path.Combine(_directory, "first");
        var secondPath = Path.Combine(_directory, "second");

        await first.RecordRecentRepositoryAsync(firstPath, "First", "main");
        await second.RecordRecentRepositoryAsync(secondPath, "Second", "dev");
        await first.CreateRepositoryGroupAsync("Work");

        var persisted = Open();
        Assert.Contains(persisted.RecentRepositories, item => item.Path == firstPath);
        Assert.Contains(persisted.RecentRepositories, item => item.Path == secondPath);
        Assert.Contains(persisted.RepositoryGroups, group => group.Name == "Work");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
