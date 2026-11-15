using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class GitToolsSettingsViewModelTests
{
    [Fact]
    public void ScopeSwitchKeepsIndependentUnsavedDrafts()
    {
        var section = new GitToolSectionViewModel(GitToolKind.Merge, "Merge Tool");
        section.Load(Snapshot(GitToolKind.Merge), repositoryAvailable: true);

        section.Value = "kdiff3";
        Assert.True(section.IsDirty);
        section.SelectedScope = section.Scopes.Single(scope => scope.Scope == GitToolWriteScope.Repository);
        section.Value = "meld";
        Assert.True(section.IsDirty);

        section.SelectedScope = section.Scopes.Single(scope => scope.Scope == GitToolWriteScope.Global);
        Assert.Equal("kdiff3", section.Value);
        section.SelectedScope = section.Scopes.Single(scope => scope.Scope == GitToolWriteScope.Repository);
        Assert.Equal("meld", section.Value);
        Assert.True(section.HasUnsavedChanges);
    }

    [Fact]
    public async Task RepositoryChangePreservesDraftButBlocksSavingToOtherRepository()
    {
        var first = Repository("first");
        var second = Repository("second");
        Repository? current = first;
        var config = new FakeConfiguration();
        var vm = new GitToolsSettingsViewModel(
            config, new FakeExternalTools(), _ => new Lease(), () => current);
        Assert.True(await vm.RefreshAsync(first));
        vm.Merge.Value = "kdiff3";

        current = second;
        vm.RepositoryChanged();

        Assert.True(vm.Merge.IsStale);
        Assert.Equal("kdiff3", vm.Merge.Value);
        Assert.False(vm.Merge.CanSave);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => vm.SaveAsync(second, vm.Merge));
        Assert.Equal(0, config.Saves);
        Assert.False(await vm.RefreshAsync(second));
        Assert.True(await vm.RefreshAsync(second, discard: true));
        Assert.False(vm.Merge.IsStale);
        Assert.False(vm.Merge.IsDirty);
    }

    [Fact]
    public async Task SaveRechecksRepositoryAfterAdmissionBeforeGitMutation()
    {
        var first = Repository("first");
        var second = Repository("second");
        Repository? current = first;
        var config = new FakeConfiguration();
        var admissions = 0;
        var vm = new GitToolsSettingsViewModel(config, new FakeExternalTools(), _ =>
        {
            if (Interlocked.Increment(ref admissions) == 2) current = second;
            return new Lease();
        }, () => current);
        Assert.True(await vm.RefreshAsync(first));
        vm.Merge.Value = "kdiff3";

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveAsync(first, vm.Merge));
        Assert.Equal(0, config.Saves);
    }

    [Fact]
    public async Task OldAsyncResultCannotOverwriteNewRepository()
    {
        var first = Repository("first");
        var second = Repository("second");
        Repository? current = first;
        var config = new FakeConfiguration();
        var gate = new TaskCompletionSource<GitToolConfigurationSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        config.NextDiffRead = gate.Task;
        var vm = new GitToolsSettingsViewModel(
            config, new FakeExternalTools(), _ => new Lease(), () => current);

        var obsoleteRead = vm.RefreshAsync(first);
        current = second;
        vm.RepositoryChanged();
        gate.SetResult(Snapshot(GitToolKind.Diff));

        Assert.False(await obsoleteRead);
        Assert.Empty(vm.Editor.Scopes);
        Assert.True(await vm.RefreshAsync(second));
        Assert.NotEmpty(vm.Editor.Scopes);
    }

    [Fact]
    public async Task FailedWriteReloadsGitAndKeepsDesiredEdits()
    {
        var repository = Repository("repo");
        var config = new FakeConfiguration { FailOnSave = true };
        var vm = new GitToolsSettingsViewModel(
            config, new FakeExternalTools(), _ => new Lease(), () => repository);
        Assert.True(await vm.RefreshAsync(repository));
        vm.Merge.Value = "kdiff3";

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => vm.SaveAsync(repository, vm.Merge));
        Assert.Contains("Some values may already have been written", failure.Message);
        Assert.Equal("kdiff3", vm.Merge.Value);
        Assert.True(vm.Merge.IsDirty);
    }

    private static Repository Repository(string name)
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "csharpgit-tools-vm-tests", name));
        return new Repository(path, path, Path.Combine(path, ".git"), false);
    }

    private static GitToolConfigurationSnapshot Snapshot(GitToolKind kind) => new(
        Kind: kind,
        EffectiveValue: null,
        EffectiveSource: GitToolConfigurationSource.NotConfigured,
        EffectiveOrigin: null,
        EffectivePath: null,
        EffectiveCommand: null,
        EffectiveTrustExitCode: null,
        EffectiveKeepBackup: null,
        ResolvedExecutable: null,
        Global: new(GitToolConfigurationSource.Global, null, null, null),
        Repository: new(GitToolConfigurationSource.Repository, null, null, null),
        Worktree: new(GitToolConfigurationSource.Worktree, null, null, null),
        System: new(GitToolConfigurationSource.System, null, null, null),
        Presets: [new("kdiff3", "KDiff3", "kdiff3"), new("meld", "Meld", "meld"),
                  new("custom", "Custom", string.Empty, IsCustom: true)],
        SupportedTools: [],
        Validation: []);

    private sealed class FakeConfiguration : IGitToolConfigurationService
    {
        public Task<GitToolConfigurationSnapshot>? NextDiffRead { get; set; }
        public bool FailOnSave { get; init; }
        public int Saves { get; private set; }

        public Task<GitToolConfigurationSnapshot> ReadAsync(
            Repository? repository, GitToolKind kind, CancellationToken cancellationToken = default)
        {
            if (kind == GitToolKind.Diff && NextDiffRead is { } pending)
            {
                NextDiffRead = null;
                return pending;
            }
            return Task.FromResult(Snapshot(kind));
        }

        public Task SaveAsync(Repository? repository, GitToolEdit edit, CancellationToken cancellationToken = default)
        {
            Saves++;
            if (FailOnSave) throw new InvalidOperationException("Simulated write failure");
            return Task.CompletedTask;
        }

        public Task RemoveOverrideAsync(Repository? repository, GitToolKind kind,
            GitToolWriteScope scope, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeExternalTools : IExternalGitToolService
    {
        public Task OpenEditorAsync(Repository? repository, string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RunExternalDiffAsync(Repository repository, DiffFileVersionPair pair, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RunMergeToolForFileAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RunMergeToolWorkflowAsync(Repository repository, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OpenConflictInEditorAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TestAsync(Repository? repository, GitToolKind kind, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Lease : IDisposable
    {
        public void Dispose() { }
    }
}
