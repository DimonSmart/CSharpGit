using System.Text.Json;
using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

public sealed class JsonAppSettingsRecentRepositoriesTests
{
    [Fact]
    public void LoadKeepsOneThousandUnpinnedAndAllPinnedRepositories()
    {
        using var fixture = new SettingsFixture();
        var now = DateTimeOffset.UtcNow;
        var entries = Enumerable.Range(0, 1005)
            .Select(index => new RecentRepositorySettings(
                fixture.RepositoryPath($"recent-{index}"),
                $"recent-{index}",
                now.AddMinutes(index),
                null))
            .Concat(Enumerable.Range(0, 7).Select(index => new RecentRepositorySettings(
                fixture.RepositoryPath($"pinned-{index}"),
                $"pinned-{index}",
                now.AddHours(-index),
                null,
                true,
                index)));
        fixture.WriteRecentRepositories(entries);

        var service = fixture.CreateService();

        Assert.Equal(1007, service.RecentRepositories.Count);
        Assert.Equal(7, service.RecentRepositories.Count(repository => repository.IsPinned));
        Assert.Equal(1000, service.RecentRepositories.Count(repository => !repository.IsPinned));
        Assert.DoesNotContain(
            service.RecentRepositories,
            repository => repository.Path == Path.GetFullPath(fixture.RepositoryPath("recent-0")));
    }

    [Fact]
    public async Task RecordingOneThousandAndFirstUnpinnedRepositoryEvictsOldestUnpinned()
    {
        using var fixture = new SettingsFixture();
        var now = DateTimeOffset.UtcNow;
        fixture.WriteRecentRepositories(Enumerable.Range(0, 1000).Select(index => new RecentRepositorySettings(
            fixture.RepositoryPath($"recent-{index}"),
            $"recent-{index}",
            now.AddMinutes(-index),
            null)));
        var service = fixture.CreateService();
        var oldestPath = Path.GetFullPath(fixture.RepositoryPath("recent-999"));

        await service.RecordRecentRepositoryAsync(
            fixture.RepositoryPath("newest"),
            "newest",
            "main");

        Assert.Equal(1000, service.RecentRepositories.Count);
        Assert.DoesNotContain(service.RecentRepositories, repository => repository.Path == oldestPath);
        Assert.Equal("newest", service.RecentRepositories.First(repository => !repository.IsPinned).DisplayName);
    }

    [Fact]
    public async Task PinPersistsAndNewPinIsAppendedToPinnedOrder()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        var first = fixture.RepositoryPath("first");
        var second = fixture.RepositoryPath("second");

        await service.RecordRecentRepositoryAsync(first, "first", "main");
        await service.RecordRecentRepositoryAsync(second, "second", "main");
        await service.SetRecentRepositoryPinnedAsync(first, true);
        await service.SetRecentRepositoryPinnedAsync(second, true);

        var restored = fixture.CreateService();
        var pinned = restored.RecentRepositories.Where(repository => repository.IsPinned).ToArray();

        Assert.Equal(2, pinned.Length);
        Assert.Equal(Path.GetFullPath(first), pinned[0].Path);
        Assert.Equal(0, pinned[0].PinnedOrder);
        Assert.Equal(Path.GetFullPath(second), pinned[1].Path);
        Assert.Equal(1, pinned[1].PinnedOrder);
    }

    [Fact]
    public async Task OpeningPinnedRepositoryPreservesPinnedOrder()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        var first = fixture.RepositoryPath("first");
        var second = fixture.RepositoryPath("second");
        await service.RecordRecentRepositoryAsync(first, "first", "old");
        await service.RecordRecentRepositoryAsync(second, "second", "main");
        await service.SetRecentRepositoryPinnedAsync(first, true);
        await service.SetRecentRepositoryPinnedAsync(second, true);
        var before = service.RecentRepositories.Single(repository => repository.Path == Path.GetFullPath(first));

        await service.RecordRecentRepositoryAsync(first, "first renamed", "feature");

        var after = service.RecentRepositories.Single(repository => repository.Path == Path.GetFullPath(first));
        Assert.True(after.IsPinned);
        Assert.Equal(before.PinnedOrder, after.PinnedOrder);
        Assert.True(after.LastOpenedUtc >= before.LastOpenedUtc);
        Assert.Equal("first renamed", after.DisplayName);
        Assert.Equal("feature", after.LastBranchName);
    }

    [Fact]
    public async Task UpdatingRecentBranchPreservesRecencyAndPinnedOrder()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        var path = fixture.RepositoryPath("repo");
        await service.RecordRecentRepositoryAsync(path, "repo", "main");
        await service.SetRecentRepositoryPinnedAsync(path, true);
        var before = service.RecentRepositories.Single();

        await service.UpdateRecentRepositoryBranchAsync(path, " feature/repository-switcher ");

        var after = service.RecentRepositories.Single();
        Assert.Equal("feature/repository-switcher", after.LastBranchName);
        Assert.Equal(before.LastOpenedUtc, after.LastOpenedUtc);
        Assert.Equal(before.IsPinned, after.IsPinned);
        Assert.Equal(before.PinnedOrder, after.PinnedOrder);
        Assert.Equal(before.DisplayName, after.DisplayName);
    }

    [Fact]
    public async Task MovePinnedRepositoryPersistsOrder()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        var paths = new[]
        {
            fixture.RepositoryPath("one"),
            fixture.RepositoryPath("two"),
            fixture.RepositoryPath("three")
        };
        foreach (var path in paths)
        {
            await service.RecordRecentRepositoryAsync(path, Path.GetFileName(path), "main");
            await service.SetRecentRepositoryPinnedAsync(path, true);
        }

        await service.MovePinnedRepositoryAsync(paths[2], 0);

        var restoredPinned = fixture.CreateService().RecentRepositories
            .Where(repository => repository.IsPinned)
            .OrderBy(repository => repository.PinnedOrder)
            .ToArray();
        Assert.Equal(
            new[] { Path.GetFullPath(paths[2]), Path.GetFullPath(paths[0]), Path.GetFullPath(paths[1]) },
            restoredPinned.Select(repository => repository.Path));
        Assert.Equal(new int?[] { 0, 1, 2 }, restoredPinned.Select(repository => repository.PinnedOrder));
    }

    [Fact]
    public async Task UnpinReturnsRepositoryToRecentOrderAndTrimsOverflow()
    {
        using var fixture = new SettingsFixture();
        var now = DateTimeOffset.UtcNow;
        var pinnedPath = fixture.RepositoryPath("pinned");
        var entries = new[]
        {
            new RecentRepositorySettings(pinnedPath, "pinned", now.AddHours(-1), null, true, 0)
        }.Concat(Enumerable.Range(0, 1000).Select(index => new RecentRepositorySettings(
            fixture.RepositoryPath($"recent-{index}"),
            $"recent-{index}",
            now.AddMinutes(-index),
            null)));
        fixture.WriteRecentRepositories(entries);
        var service = fixture.CreateService();

        await service.SetRecentRepositoryPinnedAsync(pinnedPath, false);

        Assert.Equal(1000, service.RecentRepositories.Count);
        Assert.DoesNotContain(
            service.RecentRepositories,
            repository => repository.Path == Path.GetFullPath(fixture.RepositoryPath("recent-999")));
        var unpinned = service.RecentRepositories.Single(repository => repository.Path == Path.GetFullPath(pinnedPath));
        Assert.False(unpinned.IsPinned);
        Assert.Null(unpinned.PinnedOrder);
    }

    [Fact]
    public async Task RemovingPinnedRepositoryCompactsRemainingPinnedOrder()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        var paths = new[]
        {
            fixture.RepositoryPath("one"),
            fixture.RepositoryPath("two"),
            fixture.RepositoryPath("three")
        };
        foreach (var path in paths)
        {
            await service.RecordRecentRepositoryAsync(path, Path.GetFileName(path), null);
            await service.SetRecentRepositoryPinnedAsync(path, true);
        }

        await service.RemoveRecentRepositoryAsync(paths[1]);

        var pinned = service.RecentRepositories.Where(repository => repository.IsPinned).ToArray();
        Assert.Equal(new int?[] { 0, 1 }, pinned.Select(repository => repository.PinnedOrder));
        Assert.Equal(new[] { Path.GetFullPath(paths[0]), Path.GetFullPath(paths[2]) }, pinned.Select(repository => repository.Path));
    }

    [Fact]
    public void LegacyJsonWithoutPinPropertiesLoadsAsUnpinned()
    {
        using var fixture = new SettingsFixture();
        fixture.WriteSettings(
            $$"""
            {
              "RecentRepositories": [
                {
                  "Path": {{JsonSerializer.Serialize(fixture.RepositoryPath("legacy"))}},
                  "DisplayName": "legacy",
                  "LastOpenedUtc": "2026-09-20T12:00:00+00:00",
                  "LastBranchName": "main"
                }
              ]
            }
            """);

        var repository = Assert.Single(fixture.CreateService().RecentRepositories);

        Assert.False(repository.IsPinned);
        Assert.Null(repository.PinnedOrder);
    }

    [Fact]
    public void InvalidPinnedOrdersAreNormalizedDeterministically()
    {
        using var fixture = new SettingsFixture();
        var now = DateTimeOffset.UtcNow;
        fixture.WriteRecentRepositories(new[]
        {
            new RecentRepositorySettings(fixture.RepositoryPath("b"), "b", now.AddMinutes(-2), null, true, 4),
            new RecentRepositorySettings(fixture.RepositoryPath("a"), "a", now.AddMinutes(-1), null, true, 4),
            new RecentRepositorySettings(fixture.RepositoryPath("c"), "c", now, null, true, -2),
            new RecentRepositorySettings(fixture.RepositoryPath("d"), "d", now, null, true, null),
            new RecentRepositorySettings(fixture.RepositoryPath("plain"), "plain", now, null, false, 12)
        });

        var repositories = fixture.CreateService().RecentRepositories;
        var pinned = repositories.Where(repository => repository.IsPinned).ToArray();

        Assert.Equal(new[] { "a", "b", "c", "d" }, pinned.Select(repository => repository.DisplayName));
        Assert.Equal(new int?[] { 0, 1, 2, 3 }, pinned.Select(repository => repository.PinnedOrder));
        Assert.Null(repositories.Single(repository => repository.DisplayName == "plain").PinnedOrder);
    }

    [Fact]
    public void DuplicatePathsUseNewestRecordBeforePinNormalization()
    {
        using var fixture = new SettingsFixture();
        var path = fixture.RepositoryPath("same");
        fixture.WriteRecentRepositories(new[]
        {
            new RecentRepositorySettings(path, "older pinned", DateTimeOffset.UtcNow.AddDays(-1), "old", true, 0),
            new RecentRepositorySettings(path, "newer", DateTimeOffset.UtcNow, "new", false, null)
        });

        var repository = Assert.Single(fixture.CreateService().RecentRepositories);

        Assert.Equal("newer", repository.DisplayName);
        Assert.Equal("new", repository.LastBranchName);
        Assert.False(repository.IsPinned);
    }

    [Fact]
    public void PathComparisonUsesPlatformSemantics()
    {
        using var fixture = new SettingsFixture();
        var lower = fixture.RepositoryPath("case-sensitive");
        var upper = lower.ToUpperInvariant();
        fixture.WriteRecentRepositories(new[]
        {
            new RecentRepositorySettings(lower, "lower", DateTimeOffset.UtcNow.AddMinutes(-1), null),
            new RecentRepositorySettings(upper, "upper", DateTimeOffset.UtcNow, null)
        });

        var count = fixture.CreateService().RecentRepositories.Count;

        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? 1 : 2, count);
    }

    [Fact]
    public async Task PinPersistenceFailureDoesNotPublishCandidateState()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        var path = fixture.RepositoryPath("repo");
        await service.RecordRecentRepositoryAsync(path, "repo", "main");
        Directory.CreateDirectory(fixture.SettingsPath + ".tmp");
        var changes = 0;
        service.Changed += (_, _) => changes++;

        var exception = await Record.ExceptionAsync(
            () => service.SetRecentRepositoryPinnedAsync(path, true));

        Assert.NotNull(exception);
        Assert.False(service.RecentRepositories.Single().IsPinned);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void LegacyPinnedRepositoriesMigrateToFavoritesWithoutWritingTheFile()
    {
        using var fixture = new SettingsFixture();
        fixture.WriteRecentRepositories(new[]
        {
            new RecentRepositorySettings(fixture.RepositoryPath("second"), "second",
                DateTimeOffset.UnixEpoch.AddMinutes(2), "dev", true, 1),
            new RecentRepositorySettings(fixture.RepositoryPath("first"), "first",
                DateTimeOffset.UnixEpoch.AddMinutes(1), "main", true, 0)
        });
        var before = File.ReadAllText(fixture.SettingsPath);
        var settings = fixture.CreateService();
        Assert.Equal("favorites", Assert.Single(settings.RepositoryGroups).Id);
        Assert.All(settings.RecentRepositories, entry => Assert.Equal("favorites", entry.GroupId));
        Assert.Equal(new[] { "first", "second" },
            settings.RecentRepositories.Select(entry => entry.DisplayName));
        Assert.Equal(before, File.ReadAllText(fixture.SettingsPath));
    }

    [Fact]
    public async Task GroupOperationsPreserveMembershipAndIndependentOrder()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        var a = fixture.RepositoryPath("a");
        var b = fixture.RepositoryPath("b");
        var c = fixture.RepositoryPath("c");
        foreach (var path in new[] { a, b, c })
            await service.RecordRecentRepositoryAsync(path, Path.GetFileName(path), "main");

        var work = await service.CreateRepositoryGroupAsync(" Work ");
        var personal = await service.CreateRepositoryGroupAsync("Personal");
        await service.MoveRecentRepositoryToGroupAsync(a, work.Id);
        await service.MoveRecentRepositoryToGroupAsync(b, work.Id);
        await service.MoveRecentRepositoryToGroupAsync(c, personal.Id);
        await service.MovePinnedRepositoryAsync(b, 0);
        await service.RenameRepositoryGroupAsync(work.Id, "Office");
        await service.MoveRepositoryGroupAsync(work.Id, 0);
        await service.SetRepositoryGroupExpandedAsync(work.Id, false);
        var restored = fixture.CreateService();
        Assert.Equal("Office", restored.RepositoryGroups[0].Name);
        Assert.False(restored.RepositoryGroups[0].IsExpanded);
        Assert.Equal(new[] { "b", "a" }, restored.RecentRepositories
            .Where(entry => entry.GroupId == work.Id)
            .Select(entry => entry.DisplayName));
        Assert.Equal(0, restored.RecentRepositories.Single(entry => entry.GroupId == personal.Id).PinnedOrder);

        await service.DeleteRepositoryGroupAsync(work.Id);
        Assert.DoesNotContain(service.RepositoryGroups, group => group.Id == work.Id);
        var moved = service.RecentRepositories.Where(entry => entry.GroupId == "favorites").ToArray();
        Assert.Equal(new[] { "b", "a" }, moved.Select(entry => entry.DisplayName));
        Assert.Equal(new int?[] { 0, 1 }, moved.Select(entry => entry.PinnedOrder));
        Assert.All(moved, entry => Assert.True(entry.IsPinned));
    }

    [Fact]
    public async Task InvalidGroupOperationsAndNoOpsDoNotPublishChanges()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        var count = 0;
        service.Changed += (_, _) => count++;
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRepositoryGroupAsync("   "));
        var group = await service.CreateRepositoryGroupAsync("Work");
        Assert.Equal(1, count);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRepositoryGroupAsync("work"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteRepositoryGroupAsync("favorites"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.MoveRecentRepositoryToGroupAsync(fixture.RepositoryPath("missing"), "absent"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.MoveRepositoryGroupAsync(group.Id, -1));
        await service.RenameRepositoryGroupAsync(group.Id, "Work");
        await service.SetRepositoryGroupExpandedAsync(group.Id, true);
        await service.MoveRepositoryGroupAsync(group.Id, 1);
        Assert.Equal(1, count);
    }

    [Fact]
    public void InvalidGroupFieldsAreRepairedWithoutLosingOtherSettings()
    {
        using var fixture = new SettingsFixture();
        var path = JsonSerializer.Serialize(fixture.RepositoryPath("repo"));
        fixture.WriteSettings($"""
        {
          "LoggingEnabled": true,
          "RepositoryGroups": [
            { "Id": "favorites", "Name": "Favorites", "Order": 1, "IsExpanded": false },
            { "Id": "duplicates", "Name": "Work", "Order": "invalid" },
            { "Id": "duplicates", "Name": "work", "Order": 0, "IsExpanded": true },
            { "Id": null, "Name": "", "Order": -10 },
            42
          ],
          "RecentRepositories": [
            { "Path": {{path}}, "DisplayName": "repo", "LastOpenedUtc": "2026-01-01T00:00:00Z",
              "IsPinned": true, "PinnedOrder": 0, "GroupId": { "invalid": true } }
          ]
        }
        """);
        var first = fixture.CreateService();
        var second = fixture.CreateService();
        Assert.True(first.LoggingEnabled);
        Assert.Equal(first.RepositoryGroups, second.RepositoryGroups);
        Assert.Equal(4, first.RepositoryGroups.Count);
        Assert.Equal(4, first.RepositoryGroups.Select(group => group.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(4, first.RepositoryGroups.Select(group => group.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.False(first.RepositoryGroups.Single(group => group.Id == "favorites").IsExpanded);
        var repository = Assert.Single(first.RecentRepositories);
        Assert.Equal("favorites", repository.GroupId);
    }

    [Fact]
    public async Task FailedGroupSaveDoesNotPublishCandidate()
    {
        using var fixture = new SettingsFixture();
        var service = fixture.CreateService();
        Directory.CreateDirectory(fixture.SettingsPath + ".tmp");
        var changes = 0;
        service.Changed += (_, _) => changes++;
        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateRepositoryGroupAsync("Work"));
        Assert.Single(service.RepositoryGroups);
        Assert.Equal(0, changes);
    }

    private sealed class SettingsFixture : IDisposable
    {
        public SettingsFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "csharpgit-recent-settings-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            SettingsPath = Path.Combine(DirectoryPath, "settings.json");
        }

        private string DirectoryPath { get; }

        public string SettingsPath { get; }

        public string RepositoryPath(string name) => Path.Combine(DirectoryPath, "repositories", name);

        public JsonAppSettingsService CreateService() => new(SettingsPath);

        public void WriteSettings(string json) => File.WriteAllText(SettingsPath, json);

        public void WriteRecentRepositories(IEnumerable<RecentRepositorySettings> repositories) =>
            WriteSettings(JsonSerializer.Serialize(new { RecentRepositories = repositories }));

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
    }
}
