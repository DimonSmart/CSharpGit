using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed partial class GitRepositorySyncService : IRepositorySyncService
{
    private readonly GitRepositoryCommandRunner _runner;
    private readonly GitPushExecutor _pushExecutor;
    private readonly DefaultBranchResolver _defaultBranchResolver;

    internal GitRepositorySyncService(GitCommandExecutor executor)
        : this(
            new GitRepositoryCommandRunner(executor),
            new DefaultBranchResolver(executor))
    {
    }

    internal GitRepositorySyncService(
        GitRepositoryCommandRunner runner,
        DefaultBranchResolver defaultBranchResolver)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _pushExecutor = new GitPushExecutor(_runner);
        _defaultBranchResolver = defaultBranchResolver
            ?? throw new ArgumentNullException(nameof(defaultBranchResolver));
    }

    public async Task FetchAsync(
        Repository repository,
        string remote,
        CancellationToken cancellationToken = default)
    {
        GitRefValidator.Validate(remote, nameof(remote));
        await _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "fetch",
            "--prune",
            remote);
        await _defaultBranchResolver.RefreshRemoteHeadAsync(
            repository,
            remote,
            cancellationToken);
    }

    public async Task FetchAllAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        await _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "fetch",
            "--all",
            "--prune");
        await _defaultBranchResolver.RefreshAllRemoteHeadsAsync(
            repository,
            cancellationToken);
    }

    public async Task DeleteRemoteBranchAsync(
        Repository repository,
        string remote,
        string branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        GitRefValidator.Validate(remote, nameof(remote));
        GitRefValidator.Validate(branch, nameof(branch));
        await _pushExecutor.RunAsync(
            repository,
            cancellationToken,
            "push",
            "--porcelain",
            remote,
            $":refs/heads/{branch}");
    }

    public async Task<PullResult> PullAsync(
        Repository repository,
        PullOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Strategy))
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown pull strategy.");

        var branch = await CurrentBranchAsync(repository, cancellationToken);
        var upstream = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--abbrev-ref",
            "--symbolic-full-name",
            "@{upstream}");
        if (string.IsNullOrWhiteSpace(upstream))
            throw new InvalidOperationException(
                $"Branch '{branch}' has no configured upstream. Publish the branch or configure an upstream before pulling.");

        var existingOperation = GitOperationDetector.Detect(repository);
        if (existingOperation != RepositoryOperation.None)
            return new PullResult(
                PullOutcome.NeedsAttention, PullCompletionKind.Unknown,
                existingOperation, false,
                "Finish or abort the active repository operation before pulling again.");

        var preexistingUnmerged = await _runner.RunAsync(
            repository.WorkingDirectory, cancellationToken, false,
            "ls-files", "--unmerged", "-z");
        if (!string.IsNullOrEmpty(preexistingUnmerged))
            return new PullResult(
                PullOutcome.NeedsAttention, PullCompletionKind.Unknown,
                RepositoryOperation.None, true,
                "Resolve the existing unmerged files before pulling again.");

        var before = (await _runner.RunOptionalAsync(
            repository.WorkingDirectory, cancellationToken, "rev-parse", "--verify", "HEAD")).Trim();

        var arguments = new List<string> { "pull", "--prune" };
        switch (options.Strategy)
        {
            case PullStrategy.Merge:
                arguments.Add("--no-rebase");
                arguments.Add("--ff");
                break;
            case PullStrategy.Rebase:
                arguments.Add("--rebase");
                break;
            case PullStrategy.FastForwardOnly:
                arguments.Add("--ff-only");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options), "Unknown pull strategy.");
        }
        if (options.ForceAutoStash)
            arguments.Add("--autostash");

        // FETCH_HEAD is rewritten by Git's fetch phase. A historical remote-tracking
        // divergence alone must not masquerade as a fast-forward refusal if fetch fails.
        var fetchHeadPath = Path.Combine(repository.GitDirectory, "FETCH_HEAD");
        var previousFetchHeadWrite = File.Exists(fetchHeadPath)
            ? File.GetLastWriteTimeUtc(fetchHeadPath)
            : DateTime.MinValue;

        GitCommandResult execution;
        try
        {
            execution = await _runner.RunForResultAsync(
                repository.WorkingDirectory,
                "Pull",
                GitCommandKind.User,
                cancellationToken,
                new Dictionary<string, string?> { ["GIT_MERGE_AUTOEDIT"] = "no" },
                arguments);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new PullResult(
                PullOutcome.Failed, PullCompletionKind.Unknown,
                GitOperationDetector.Detect(repository), false, exception.Message);
        }

        // Git can finish the integration but fail to apply its autostash.
        // Operation markers and the index, not localized CLI text, determine attention state.
        var activeOperation = GitOperationDetector.Detect(repository);
        var unresolved = await _runner.RunAsync(
            repository.WorkingDirectory, cancellationToken, false,
            "ls-files", "--unmerged", "-z");
        var hasUnmergedPaths = !string.IsNullOrEmpty(unresolved);
        var diagnostic = string.IsNullOrWhiteSpace(execution.StandardError)
            ? execution.StandardOutput.Trim()
            : execution.StandardError.Trim();
        if (activeOperation != RepositoryOperation.None || hasUnmergedPaths)
        {
            var description = activeOperation == RepositoryOperation.Rebase && !hasUnmergedPaths
                ? "Rebase is paused. Use the repository operation controls to continue or abort."
                : activeOperation == RepositoryOperation.None
                    ? "Unmerged files remain in the working tree. Resolve them before continuing."
                    : "Resolve the conflicts using the repository operation controls.";
            return new PullResult(
                PullOutcome.NeedsAttention, PullCompletionKind.Unknown,
                activeOperation, hasUnmergedPaths,
                string.IsNullOrWhiteSpace(diagnostic) ? description : $"{description}\n{diagnostic}");
        }

        if (execution.ExitCode != 0)
        {
            var refused = options.Strategy == PullStrategy.FastForwardOnly
                          && !string.IsNullOrWhiteSpace(before)
                          && await HasDivergedAsync(
                              repository, before, upstream.Trim(),
                              fetchHeadPath, previousFetchHeadWrite, cancellationToken);
            return new PullResult(
                refused ? PullOutcome.Refused : PullOutcome.Failed,
                PullCompletionKind.Unknown, RepositoryOperation.None, false,
                refused
                    ? "Fast-forward is not possible because the local and upstream branches have diverged. Choose Merge or Rebase."
                    : $"Git pull failed (exit {execution.ExitCode}): {diagnostic}");
        }

        var after = (await _runner.RunOptionalAsync(
            repository.WorkingDirectory, cancellationToken, "rev-parse", "--verify", "HEAD")).Trim();
        var completion = await ClassifyPullCompletionAsync(
            repository, options.Strategy, before, after, cancellationToken);
        return new PullResult(
            PullOutcome.Completed, completion, RepositoryOperation.None, false,
            string.IsNullOrWhiteSpace(diagnostic) ? "Git pull completed." : diagnostic);
    }

    private async Task<bool> HasDivergedAsync(
        Repository repository,
        string localHead,
        string upstream,
        string fetchHeadPath,
        DateTime previousFetchHeadWrite,
        CancellationToken cancellationToken)
    {
        var remoteHead = (await _runner.RunOptionalAsync(
            repository.WorkingDirectory, cancellationToken,
            "rev-parse", "--verify", upstream)).Trim();
        if (string.IsNullOrEmpty(remoteHead))
            return false;

        // The fetch phase must have produced a fresh FETCH_HEAD for this upstream.
        // Transport/authentication failures can leave an older divergent tracking ref.
        try
        {
            if (!File.Exists(fetchHeadPath)
                || File.GetLastWriteTimeUtc(fetchHeadPath) <= previousFetchHeadWrite
                || !File.ReadLines(fetchHeadPath).Any(line =>
                    line.StartsWith(remoteHead + "\t", StringComparison.Ordinal)))
                return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        async Task<bool?> IsAncestorAsync(string ancestor, string descendant)
        {
            var result = await _runner.RunForResultAsync(
                repository.WorkingDirectory, "PullAncestry",
                GitCommandKind.Internal, cancellationToken, null,
                ["merge-base", "--is-ancestor", ancestor, descendant]);
            return result.ExitCode switch { 0 => true, 1 => false, _ => null };
        }

        var localIsAncestor = await IsAncestorAsync(localHead, remoteHead);
        var remoteIsAncestor = await IsAncestorAsync(remoteHead, localHead);
        return localIsAncestor == false && remoteIsAncestor == false;
    }

    private async Task<PullCompletionKind> ClassifyPullCompletionAsync(
        Repository repository, PullStrategy strategy, string before, string after,
        CancellationToken cancellationToken)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return PullCompletionKind.UpToDate;
        if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after))
            return PullCompletionKind.Unknown;

        var parents = await _runner.RunAsync(
            repository.WorkingDirectory, cancellationToken, false,
            "show", "-s", "--format=%P", "HEAD");
        var parentIds = parents.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parentIds.Length > 1
            && string.Equals(parentIds[0], before, StringComparison.Ordinal)
            && strategy != PullStrategy.Rebase)
            return PullCompletionKind.MergeCommit;

        var ancestry = await _runner.RunForResultAsync(
            repository.WorkingDirectory, "PullAncestry",
            GitCommandKind.Internal, cancellationToken, null,
            ["merge-base", "--is-ancestor", before, after]);
        // A remote fast-forward target may itself be a merge commit.
        if (ancestry.ExitCode == 0)
            return PullCompletionKind.FastForward;
        // Git completed an integration without preserving the previous HEAD in the
        // new history. This is the characteristic topology of a successful rebase.
        if (ancestry.ExitCode == 1 && parentIds.Length <= 1)
            return PullCompletionKind.Rebased;
        return PullCompletionKind.Unknown;
    }

    public Task PushAsync(
        Repository repository,
        PushOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return _pushExecutor.RunAsync(
            repository,
            cancellationToken,
            options?.AutoSetupRemote == true
                ? [
                    "-c",
                    "push.autoSetupRemote=true",
                    "push",
                    "--porcelain"
                ]
                : [
                    "push",
                    "--porcelain"
                ]);
    }

    public async Task<PublishBranchPreparation> PreparePublishBranchAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var localBranch = await CurrentBranchAsync(repository, cancellationToken);
        var remotes = await ReadRemoteNamesAsync(repository, cancellationToken);

        var candidates = new[]
        {
            await ReadConfigAsync(repository, $"branch.{localBranch}.pushRemote", cancellationToken),
            await ReadConfigAsync(repository, "remote.pushDefault", cancellationToken),
            await ReadConfigAsync(repository, $"branch.{localBranch}.remote", cancellationToken)
        };

        var suggested = candidates.FirstOrDefault(candidate =>
            candidate is not null && remotes.Contains(candidate, StringComparer.Ordinal));

        if (suggested is null && remotes.Contains("origin", StringComparer.Ordinal))
            suggested = "origin";
        if (suggested is null && remotes.Count == 1)
            suggested = remotes[0];

        return new PublishBranchPreparation(localBranch, suggested);
    }

    public async Task PublishBranchAsync(
        Repository repository,
        PublishBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);

        var localBranch = await CurrentBranchAsync(repository, cancellationToken);
        var remote = request.Remote.Trim();
        var remoteBranch = request.RemoteBranch.Trim();
        GitRefValidator.Validate(remote, nameof(request.Remote));
        GitRefValidator.Validate(remoteBranch, nameof(request.RemoteBranch));
        await EnsureRemoteExistsAsync(repository, remote, cancellationToken);

        var refspec = $"refs/heads/{localBranch}:refs/heads/{remoteBranch}";
        await _pushExecutor.RunAsync(
            repository,
            cancellationToken,
            request.SetUpstream
                ? [
                    "push",
                    "--porcelain",
                    "--set-upstream",
                    remote,
                    refspec
                ]
                : [
                    "push",
                    "--porcelain",
                    remote,
                    refspec
                ]);
    }

    private async Task<IReadOnlyList<string>> ReadRemoteNamesAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        var output = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "remote");

        return output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<string?> ReadConfigAsync(
        Repository repository,
        string key,
        CancellationToken cancellationToken)
    {
        var value = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "config",
            "--get",
            key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task<string> CurrentBranchAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        var branch = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "symbolic-ref",
            "--quiet",
            "--short",
            "HEAD");

        if (string.IsNullOrWhiteSpace(branch))
            throw new InvalidOperationException(
                "HEAD is detached. This operation requires a current local branch.");

        return branch.Trim();
    }
}
