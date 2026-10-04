using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitStashService : IStashService
{
    private readonly IHistoryService _history;

    internal GitStashService(IHistoryService history)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public async Task<StashDetails> ReadAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(stash);
        GitRefValidator.ValidateObjectId(stash.Commit, nameof(stash));

        var workingTree = await _history.ReadCommitAsync(
            repository,
            stash.Commit,
            cancellationToken);
        var parents = workingTree.Commit.Parents;
        if (parents.Count < 2)
            throw new InvalidOperationException(
                "The selected commit does not have the standard Git stash structure.");

        var baseCommit = parents[0];
        var indexCommit = parents[1];
        var untrackedCommit = parents.Count >= 3 ? parents[2] : null;

        var stagedTask = _history.ReadChangedFilesAsync(
            repository,
            indexCommit,
            baseCommit,
            cancellationToken);
        var unstagedTask = _history.ReadChangedFilesAsync(
            repository,
            stash.Commit,
            indexCommit,
            cancellationToken);
        var untrackedTask = untrackedCommit is null
            ? Task.FromResult<IReadOnlyList<ChangedFile>>([])
            : _history.ReadChangedFilesAsync(
                repository,
                untrackedCommit,
                parentHash: null,
                cancellationToken);

        await Task.WhenAll(stagedTask, unstagedTask, untrackedTask);

        var aggregates = new List<StashFileAggregate>();
        Merge(aggregates, workingTree.Files, StashFileSource.Combined);
        Merge(aggregates, await stagedTask, StashFileSource.Staged);
        Merge(aggregates, await unstagedTask, StashFileSource.Unstaged);
        Merge(aggregates, await untrackedTask, StashFileSource.Untracked);

        var changes = aggregates
            .Select(aggregate => aggregate.ToChangedFile())
            .OrderBy(change => change.File.Path, StringComparer.Ordinal)
            .ToArray();

        return new StashDetails(
            stash,
            workingTree.Commit,
            baseCommit,
            indexCommit,
            untrackedCommit,
            changes);
    }

    private static void Merge(
        List<StashFileAggregate> aggregates,
        IReadOnlyList<ChangedFile> files,
        StashFileSource source)
    {
        foreach (var file in files)
        {
            var matches = aggregates
                .Where(aggregate => aggregate.Overlaps(file))
                .ToArray();
            StashFileAggregate target;
            if (matches.Length == 0)
            {
                target = new StashFileAggregate();
                aggregates.Add(target);
            }
            else
            {
                target = matches[0];
                foreach (var duplicate in matches.Skip(1))
                {
                    target.MergeFrom(duplicate);
                    aggregates.Remove(duplicate);
                }
            }

            target.Add(file, source);
        }
    }

    private enum StashFileSource
    {
        Combined,
        Staged,
        Unstaged,
        Untracked
    }

    private sealed class StashFileAggregate
    {
        private readonly HashSet<string> _paths = new(StringComparer.Ordinal);
        private ChangedFile? _combined;
        private ChangedFile? _staged;
        private ChangedFile? _unstaged;
        private ChangedFile? _untracked;

        public bool Overlaps(ChangedFile file) =>
            EnumeratePaths(file).Any(_paths.Contains);

        public void Add(ChangedFile file, StashFileSource source)
        {
            foreach (var path in EnumeratePaths(file))
                _paths.Add(path);

            switch (source)
            {
                case StashFileSource.Combined:
                    _combined = PreferRenameAware(_combined, file);
                    break;
                case StashFileSource.Staged:
                    _staged = PreferRenameAware(_staged, file);
                    break;
                case StashFileSource.Unstaged:
                    _unstaged = PreferRenameAware(_unstaged, file);
                    break;
                case StashFileSource.Untracked:
                    _untracked = PreferRenameAware(_untracked, file);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(source));
            }
        }

        public void MergeFrom(StashFileAggregate other)
        {
            foreach (var path in other._paths)
                _paths.Add(path);
            if (other._combined is not null) _combined = PreferRenameAware(_combined, other._combined);
            if (other._staged is not null) _staged = PreferRenameAware(_staged, other._staged);
            if (other._unstaged is not null) _unstaged = PreferRenameAware(_unstaged, other._unstaged);
            if (other._untracked is not null) _untracked = PreferRenameAware(_untracked, other._untracked);
        }

        public StashChangedFile ToChangedFile()
        {
            if (_untracked is not null)
                return new StashChangedFile(
                    _untracked,
                    StashChangeState.Untracked,
                    HasCombinedDiff: true);

            var state = (_staged is not null, _unstaged is not null) switch
            {
                (true, true) => StashChangeState.StagedAndUnstaged,
                (true, false) => StashChangeState.Staged,
                (false, true) => StashChangeState.Unstaged,
                _ => throw new InvalidOperationException("Tracked stash change has no staged or unstaged component.")
            };

            var source = _combined ?? PreferRenameAware(_staged, _unstaged)
                ?? throw new InvalidOperationException("Stash change has no file metadata.");
            var file = _combined is null
                ? source with { AddedLines = 0, RemovedLines = 0 }
                : source;
            return new StashChangedFile(
                file,
                state,
                HasCombinedDiff: _combined is not null);
        }

        private static ChangedFile? PreferRenameAware(ChangedFile? current, ChangedFile? candidate)
        {
            if (candidate is null) return current;
            if (current is null || current.OriginalPath is null && candidate.OriginalPath is not null)
                return candidate;
            return current;
        }

        private static IEnumerable<string> EnumeratePaths(ChangedFile file)
        {
            yield return file.Path;
            if (file.OriginalPath is not null
                && !string.Equals(file.OriginalPath, file.Path, StringComparison.Ordinal))
                yield return file.OriginalPath;
        }
    }
}
