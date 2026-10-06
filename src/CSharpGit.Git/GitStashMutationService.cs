using System.Text;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitStashMutationService : IStashMutationService
{
    private readonly GitRepositoryCommandRunner _runner;

    internal GitStashMutationService(GitRepositoryCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public Task CreateStashAsync(
        Repository repository,
        string? message = null,
        CancellationToken cancellationToken = default) =>
        CreateStashAsync(
            repository,
            new CreateStashRequest(message, StashScope.AllTrackedChanges),
            cancellationToken);

    public async Task CreateStashAsync(
        Repository repository,
        CreateStashRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);

        var arguments = new List<string> { "stash", "push" };
        string? pathspecFile = null;
        try
        {
            switch (request.Scope)
            {
                case StashScope.AllTrackedChanges:
                    EnsureNoSelectedPaths(request);
                    if (request.IncludeUntracked)
                        arguments.Add("--include-untracked");
                    break;

                case StashScope.StagedChangesOnly:
                    EnsureNoSelectedPaths(request);
                    if (request.IncludeUntracked)
                        throw new ArgumentException(
                            "Staged-only stash cannot include untracked files.",
                            nameof(request));
                    arguments.Add("--staged");
                    break;

                case StashScope.SelectedPaths:
                    if (request.IncludeUntracked)
                        throw new ArgumentException(
                            "Selected-path stash determines untracked handling from the selected paths.",
                            nameof(request));

                    var selectedPaths = ExpandSelectedPaths(request.Paths);
                    if (selectedPaths.Count == 0)
                        throw new ArgumentException(
                            "Selected-path stash requires at least one path.",
                            nameof(request));

                    if (request.Paths!.Any(path => path.IsUntracked))
                        arguments.Add("--include-untracked");

                    var pathspecEntries = await BuildCompactSelectedPathspecAsync(
                        repository,
                        selectedPaths,
                        cancellationToken);

                    pathspecFile = await WritePathspecFileAsync(
                        pathspecEntries,
                        cancellationToken);
                    arguments.Add($"--pathspec-from-file={pathspecFile}");
                    arguments.Add("--pathspec-file-nul");
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }

            if (!string.IsNullOrWhiteSpace(request.Message))
            {
                arguments.Add("--message");
                arguments.Add(request.Message.Trim());
            }

            await _runner.RunMutationAsync(
                repository,
                cancellationToken,
                arguments.ToArray());
        }
        finally
        {
            DeleteTemporaryPathspecFile(pathspecFile);
        }
    }

    public Task ApplyStashAsync(
        Repository repository,
        string stashName,
        CancellationToken cancellationToken = default)
    {
        ValidateStashName(stashName);
        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "stash",
            "apply",
            stashName);
    }

    public Task ApplyStashAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stash);
        GitRefValidator.ValidateObjectId(stash.Commit, nameof(stash));
        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "stash",
            "apply",
            stash.Commit);
    }

    public Task PopStashAsync(
        Repository repository,
        string stashName,
        CancellationToken cancellationToken = default)
    {
        ValidateStashName(stashName);
        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "stash",
            "pop",
            stashName);
    }

    public async Task PopStashAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentStashIdentityAsync(repository, stash, cancellationToken);
        await _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "stash",
            "pop",
            stash.Name);
    }

    public async Task DropStashAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentStashIdentityAsync(repository, stash, cancellationToken);
        await _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "stash",
            "drop",
            stash.Name);
    }

    private async Task EnsureCurrentStashIdentityAsync(
        Repository repository,
        GitStash stash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stash);
        ValidateStashName(stash.Name);
        GitRefValidator.ValidateObjectId(stash.Commit, nameof(stash));

        var resolved = (await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--verify",
            $"{stash.Name}^{{commit}}")).Trim();

        if (!string.Equals(resolved, stash.Commit, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The stash list changed outside CSharpGit. Refresh the repository and try again.");
    }

    private static void EnsureNoSelectedPaths(CreateStashRequest request)
    {
        if (request.Paths is { Count: > 0 })
            throw new ArgumentException(
                "Selected paths are valid only for SelectedPaths stash scope.",
                nameof(request));
    }

    private static IReadOnlyList<string> ExpandSelectedPaths(
        IReadOnlyList<StashSelectedPath>? paths)
    {
        if (paths is null || paths.Count == 0)
            return [];

        var result = new List<string>(paths.Count * 2);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selected in paths)
        {
            AddPath(selected.Path);
            if (!string.IsNullOrWhiteSpace(selected.OriginalPath))
                AddPath(selected.OriginalPath!);
        }
        return result;

        void AddPath(string path)
        {
            var validated = ValidateRepositoryRelativePath(path);
            if (seen.Add(validated))
                result.Add(validated);
        }
    }

    private static string ValidateRepositoryRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path)
            || path.IndexOf('\0') >= 0
            || Path.IsPathRooted(path))
            throw new ArgumentException("Stash path must be a non-empty repository-relative path.", nameof(path));

        var normalized = path.Replace('\\', '/');
        if (normalized == ".."
            || normalized.StartsWith("../", StringComparison.Ordinal)
            || normalized.EndsWith("/..", StringComparison.Ordinal)
            || normalized.Contains("/../", StringComparison.Ordinal))
            throw new ArgumentException("Stash path must not escape the repository.", nameof(path));

        return normalized;
    }

    private async Task<IReadOnlyList<string>> BuildCompactSelectedPathspecAsync(
        Repository repository,
        IReadOnlyList<string> selectedPaths,
        CancellationToken cancellationToken)
    {
        var direct = selectedPaths
            .Select(path => $":(literal){path}")
            .ToArray();

        var statusOutput = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            GitRepositoryStateService.ReadOnlyEnvironment,
            "status",
            "--porcelain=v2",
            "-z",
            "--untracked-files=all");
        var currentChanges = GitRepositoryStateService.ParseStatusV2(statusOutput).Changes;
        var selected = selectedPaths.ToHashSet(StringComparer.Ordinal);

        // Git stash accepts --pathspec-from-file, but internally it can still pass the
        // parsed pathspec to child Git processes. On Windows a large direct selection
        // can therefore hit the process command-line limit. Build the equivalent
        // "everything except unselected changed paths" form and use whichever
        // representation is smaller. The complement form also keeps both sides of a
        // staged rename without passing an unmatched positive OriginalPath pathspec.
        var complement = new List<string> { ":(top,glob)**" };
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in currentChanges)
        {
            if (EnumerateChangePaths(change).Any(selected.Contains))
                continue;

            foreach (var path in EnumerateChangePaths(change))
            {
                var validated = ValidateRepositoryRelativePath(path);
                if (excluded.Add(validated))
                    complement.Add($":(top,literal,exclude){validated}");
            }
        }

        return PathspecCost(complement) < PathspecCost(direct)
            ? complement
            : direct;
    }

    private static long PathspecCost(IEnumerable<string> entries) =>
        entries.Sum(entry => (long)Encoding.UTF8.GetByteCount(entry) + 1);

    private static IEnumerable<string> EnumerateChangePaths(WorkingTreeChange change)
    {
        yield return change.Path;
        if (!string.IsNullOrWhiteSpace(change.OriginalPath)
            && !string.Equals(change.OriginalPath, change.Path, StringComparison.Ordinal))
            yield return change.OriginalPath!;
    }

    private static async Task<string> WritePathspecFileAsync(
        IReadOnlyList<string> entries,
        CancellationToken cancellationToken)
    {
        var file = Path.Combine(
            Path.GetTempPath(),
            $"csharpgit-stash-{Guid.NewGuid():N}.pathspec");
        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.Append(entry);
            builder.Append('\0');
        }

        try
        {
            await File.WriteAllBytesAsync(
                file,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString()),
                cancellationToken);
            return file;
        }
        catch
        {
            DeleteTemporaryPathspecFile(file);
            throw;
        }
    }

    private static void DeleteTemporaryPathspecFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ValidateStashName(string value)
    {
        if (!value.StartsWith("stash@{", StringComparison.Ordinal)
            || !value.EndsWith('}')
            || !int.TryParse(
                value.AsSpan(7, value.Length - 8),
                out var index)
            || index < 0)
            throw new ArgumentException(
                "Invalid stash reference.",
                nameof(value));
    }
}
