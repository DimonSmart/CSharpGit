using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using Microsoft.Extensions.Logging;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel
{
    private readonly IStashService? _stashService;
    private CancellationTokenSource? _stashDetailsCts;
    private long _stashDetailsGeneration;
    private StashDetails? _selectedStashDetails;
    private bool _isNoNetStashDiff;

    public StashDetails? SelectedStashDetails
    {
        get => _selectedStashDetails;
        private set
        {
            if (ReferenceEquals(_selectedStashDetails, value)) return;
            _selectedStashDetails = value;
            Notify();
            Notify(nameof(SelectedStashBaseDisplay));
            Notify(nameof(SelectedStashStatsDisplay));
            Notify(nameof(SelectedStashDetailsStatus));
            Notify(nameof(SelectedDiffCommitHash));
        }
    }

    public bool HasSelectedStash => SelectedStash is not null;
    public bool HasSelectedDetailsObject => SelectedStash is not null || SelectedHistoryRow is not null;
    public string? SelectedObjectCommit => SelectedStash?.Commit ?? SelectedHistoryRow?.Commit.Hash;
    public string SelectedDetailsTitle => SelectedStash is null ? "Commit" : "Stash";
    public bool CanMutateSelectedStash => SelectedStash is not null && CanMutate();

    internal bool CanMutateStash(GitStash stash) => stash is not null && CanMutate();

    public string SelectedStashDisplay => SelectedStash?.Display ?? string.Empty;

    public string SelectedStashHashDisplay =>
        SelectedStash is null
            ? string.Empty
            : $"Stash: {ShortHash(SelectedStash.Commit)}";

    public string SelectedStashBaseDisplay =>
        SelectedStashDetails is null
            ? "Base: loading…"
            : $"Base: {ShortHash(SelectedStashDetails.BaseCommit)}";

    public string SelectedStashStatsDisplay =>
        SelectedStashDetails is null
            ? string.Empty
            : $"{SelectedStashDetails.Changes.Count} files changed    +{SelectedStashDetails.AddedLines} -{SelectedStashDetails.RemovedLines}";

    public string SelectedStashDetailsStatus =>
        SelectedStashDetails is null
            ? "Loading stash structure…"
            : SelectedStashDetails.UntrackedCommit is null
                ? "Tracked repository snapshot saved in this stash."
                : "Tracked repository snapshot saved in this stash. Untracked stash files are shown in Changes.";

    public bool IsNoNetStashDiff
    {
        get => _isNoNetStashDiff;
        private set
        {
            if (_isNoNetStashDiff == value) return;
            _isNoNetStashDiff = value;
            Notify();
        }
    }

    public string? SelectedDiffCommitHash
    {
        get
        {
            if (SelectedStash is null)
                return SelectedHistoryRow?.Commit.Hash;

            if (SelectedStashDetails is null || SelectedFile is null)
                return SelectedStash.Commit;

            var change = FindSelectedStashChange(SelectedFile);
            return change?.State == StashChangeState.Untracked
                ? SelectedStashDetails.UntrackedCommit
                : SelectedStash.Commit;
        }
    }

    internal bool CanCreateStashRequest(
        StashScope scope,
        bool includeUntracked)
    {
        if (!CanCreateStash) return false;

        return scope switch
        {
            StashScope.AllTrackedChanges =>
                Changes.Any(change => change.Kind != FileChangeKind.Untracked)
                || includeUntracked && Changes.Any(change => change.Kind == FileChangeKind.Untracked),
            StashScope.StagedChangesOnly =>
                !includeUntracked && Changes.Any(change => change.IsStaged),
            _ => false
        };
    }

    internal bool CanCreateSelectedStash(
        IReadOnlyCollection<WorkingTreeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return CanCreateStash && changes.Count > 0;
    }

    public async Task CreateStashAsync(CreateStashRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanCreateStash) return;

        await MutateAsync(
            () => _stashMutationService.CreateStashAsync(
                Repository!,
                request),
            "Could not create stash");
    }

    internal Task CreateSelectedStashAsync(
        IReadOnlyCollection<WorkingTreeChange> changes,
        string? message)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!CanCreateSelectedStash(changes))
            return Task.CompletedTask;

        var snapshot = changes
            .Select(change => new StashSelectedPath(
                change.Path,
                change.OriginalPath,
                change.Kind == FileChangeKind.Untracked))
            .ToArray();

        return CreateStashAsync(
            new CreateStashRequest(
                message,
                StashScope.SelectedPaths,
                snapshot));
    }

    public async Task SelectStashAsync(GitStash stash)
    {
        ArgumentNullException.ThrowIfNull(stash);

        var sameIdentity = string.Equals(
            SelectedStash?.Commit,
            stash.Commit,
            StringComparison.Ordinal);

        if (!sameIdentity)
        {
            InvalidateChangedFilesLoad();
            InvalidateDiffLoad();
            SelectedChangedFiles = [];
            SelectedFile = null;
            SelectedDiff = null;
            DiffLoadErrorMessage = null;
            IsChangedFilesLoading = false;
            IsDiffLoading = false;
            ClearNoNetStashDiff();
            SelectedStashDetails = null;
        }

        SelectedHistoryRow = null;
        SelectedStash = stash;

        if (sameIdentity && SelectedStashDetails is { } current)
        {
            if (!ReferenceEquals(current.Stash, stash))
                SelectedStashDetails = current with { Stash = stash };
            if (IsChangesViewActive)
                PublishSelectedStashChanges(SelectedStashDetails);
            return;
        }

        await EnsureSelectedStashDetailsLoadedAsync();
    }

    internal void ClearSelectedStashSelection()
    {
        if (SelectedStash is null && SelectedStashDetails is null) return;

        InvalidateStashDetailsLoad();
        SelectedStashDetails = null;
        SelectedStash = null;
        ClearNoNetStashDiff();
    }

    private async Task ApplySelectedStashAsync()
    {
        var stash = SelectedStash;
        if (stash is null || !CanMutateSelectedStash) return;

        await MutateAsync(
            () => _stashMutationService.ApplyStashAsync(
                Repository!,
                stash),
            "Could not apply stash");
    }

    private async Task PopSelectedStashAsync()
    {
        var stash = SelectedStash;
        if (stash is null || !CanMutateSelectedStash) return;

        await MutateAsync(
            () => _stashMutationService.PopStashAsync(
                Repository!,
                stash),
            "Could not pop stash");
    }

    private async Task DropSelectedStashAsync()
    {
        var stash = SelectedStash;
        if (stash is null || !CanMutateSelectedStash) return;

        await MutateAsync(
            () => _stashMutationService.DropStashAsync(
                Repository!,
                stash),
            "Could not drop stash");
    }

    private async Task RestoreSelectedStashAfterRefreshAsync(
        string? selectedCommit,
        int previousIndex)
    {
        if (selectedCommit is null) return;

        var surviving = Stashes.FirstOrDefault(stash =>
            string.Equals(stash.Commit, selectedCommit, StringComparison.Ordinal));
        if (surviving is not null)
        {
            SelectedStash = surviving;
            if (SelectedStashDetails is { } details
                && string.Equals(details.Stash.Commit, surviving.Commit, StringComparison.Ordinal))
                SelectedStashDetails = details with { Stash = surviving };
            return;
        }

        InvalidateStashDetailsLoad();
        SelectedStashDetails = null;
        ClearNoNetStashDiff();

        GitStash? fallback = null;
        if (previousIndex >= 0 && previousIndex < Stashes.Count)
            fallback = Stashes[previousIndex];
        else if (previousIndex > 0 && previousIndex - 1 < Stashes.Count)
            fallback = Stashes[previousIndex - 1];

        SelectedStash = fallback;
        if (fallback is not null)
            await EnsureSelectedStashDetailsLoadedAsync();
    }

    internal async Task EnsureSelectedStashDetailsLoadedAsync()
    {
        var repository = Repository;
        var stash = SelectedStash;
        if (repository is null || stash is null || _stashService is null)
            return;

        if (SelectedStashDetails is { } current
            && string.Equals(current.Stash.Commit, stash.Commit, StringComparison.Ordinal))
        {
            if (IsChangesViewActive)
                PublishSelectedStashChanges(current);
            return;
        }

        var generation = Interlocked.Increment(ref _stashDetailsGeneration);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _stashDetailsCts, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        if (IsChangesViewActive)
            IsChangedFilesLoading = true;

        try
        {
            var details = await _stashService.ReadAsync(
                repository,
                stash,
                cancellation.Token);

            if (cancellation.IsCancellationRequested
                || generation != Volatile.Read(ref _stashDetailsGeneration)
                || !ReferenceEquals(repository, Repository)
                || !string.Equals(stash.Commit, SelectedStash?.Commit, StringComparison.Ordinal))
                return;

            SelectedStashDetails = details with { Stash = SelectedStash! };
            if (IsChangesViewActive)
                PublishSelectedStashChanges(SelectedStashDetails);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _stashDetailsGeneration)
                && ReferenceEquals(repository, Repository)
                && string.Equals(stash.Commit, SelectedStash?.Commit, StringComparison.Ordinal))
            {
                SelectedStashDetails = null;
                SelectedChangedFiles = [];
                SelectedFile = null;
                SelectedDiff = null;
                ErrorMessage = $"Could not read stash: {exception.Message}";
                _logger.LogWarning(
                    exception,
                    "Stash details loading failed for {StashCommit}",
                    stash.Commit);
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref _stashDetailsGeneration)
                && IsChangesViewActive)
                IsChangedFilesLoading = false;
        }
    }

    private void PublishSelectedStashChanges(StashDetails details)
    {
        if (!IsChangesViewActive
            || !string.Equals(details.Stash.Commit, SelectedStash?.Commit, StringComparison.Ordinal))
            return;

        var files = details.Changes
            .Select(change => change.File)
            .ToArray();
        var restored = _selectedChangedFileRestoreKey is { } restoreKey
            ? files.FirstOrDefault(restoreKey.Matches)
            : null;
        _selectedChangedFileRestoreKey = null;
        var target = restored ?? files.FirstOrDefault();

        if (!ReferenceEquals(SelectedFile, target))
            SelectedFile = target;
        SelectedChangedFiles = files;

        if (target is not null
            && SelectedDiff is null
            && !IsDiffLoading
            && !IsDiffPreviewDeferred
            && !IsNoNetStashDiff)
            StartSelectedDiffPreview();
    }

    internal string GetChangedFileDisplayStatus(ChangedFile file)
    {
        var stashChange = FindSelectedStashChange(file);
        if (stashChange is null)
            return file.Status;

        var gitStatus = stashChange.State == StashChangeState.Untracked
            ? "?"
            : file.Status;
        return $"{gitStatus}  {stashChange.StateLabel}";
    }

    internal StashChangedFile? FindSelectedStashChange(ChangedFile file)
    {
        if (SelectedStashDetails is null) return null;

        return SelectedStashDetails.Changes.FirstOrDefault(change =>
            SameLogicalPath(change.File, file));
    }

    private static bool SameLogicalPath(ChangedFile left, ChangedFile right)
    {
        var leftPaths = new[] { left.Path, left.OriginalPath }
            .Where(path => path is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        return new[] { right.Path, right.OriginalPath }
            .Where(path => path is not null)
            .Cast<string>()
            .Any(leftPaths.Contains);
    }

    private void SetNoNetStashDiff()
    {
        SelectedDiff = null;
        DiffLoadErrorMessage = null;
        IsDiffLoading = false;
        ClearDiffPreviewDeferred();
        IsNoNetStashDiff = true;
    }

    private void ClearNoNetStashDiff() => IsNoNetStashDiff = false;

    private void InvalidateStashDetailsLoad()
    {
        Interlocked.Increment(ref _stashDetailsGeneration);
        var cancellation = Interlocked.Exchange(ref _stashDetailsCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void ResetStashPresentationSession()
    {
        InvalidateStashDetailsLoad();
        SelectedStashDetails = null;
        SelectedStash = null;
        ClearNoNetStashDiff();
    }

    private static string ShortHash(string hash) =>
        hash[..Math.Min(8, hash.Length)];
}
