using CSharpGit.Domain;
using Microsoft.Extensions.Logging;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel
{
    internal async Task<HistoryRow?> EnsureHistoryCommitVisibleAsync(
        string hash,
        int trailingCount = 100)
    {
        if (Repository is null) return null;

        var repository = Repository;
        var requiresReload = _selectedScope != Scopes[0] || !string.IsNullOrWhiteSpace(_filterText);

        if (_selectedScope != Scopes[0])
        {
            _selectedScope = Scopes[0];
            Notify(nameof(SelectedScope));
        }

        if (!string.IsNullOrEmpty(_filterText))
        {
            _filterText = string.Empty;
            Notify(nameof(FilterText));
        }

        if (!requiresReload && History.FirstOrDefault(row =>
                string.Equals(row.Commit.Hash, hash, StringComparison.Ordinal)) is { } existing)
        {
            SelectedHistoryRow = existing;
            return existing;
        }

        var generation = Interlocked.Increment(ref _historyLoadGeneration);
        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _historyLoadCts, cancellation);
        if (previousCancellation is not null)
        {
            previousCancellation.Cancel();
            previousCancellation.Dispose();
        }

        EnterBusy();
        ErrorMessage = null;
        try
        {
            var page = await _historyService.ReadHistoryThroughCommitAsync(
                repository,
                HistoryScope.AllReferences,
                hash,
                trailingCount,
                cancellation.Token);

            if (cancellation.IsCancellationRequested
                || generation != Volatile.Read(ref _historyLoadGeneration)
                || !ReferenceEquals(repository, Repository))
                return null;

            Replace(History, page.Rows);
            HasMore = page.HasMore;

            var target = History.FirstOrDefault(row =>
                string.Equals(row.Commit.Hash, hash, StringComparison.Ordinal));
            if (target is null)
                throw new InvalidOperationException($"Commit {hash} was not present after history navigation load.");

            SelectedHistoryRow = target;
            return target;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _historyLoadGeneration))
            {
                ErrorMessage = $"Could not navigate to commit: {exception.Message}";
                _logger.LogWarning(exception, "History reference navigation failed for {CommitHash}", hash);
            }
            return null;
        }
        finally
        {
            ExitBusy();
        }
    }
}
