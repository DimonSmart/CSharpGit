using System.Text;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitRepositoryWorkflowService : IRepositoryWorkflowService
{
    private const string ManagedRebaseMode = "managed";
    private const string RawRebaseMode = "raw";
    private const string RebaseModeFileName = "mode";

    private readonly GitRepositoryCommandRunner _runner;
    private readonly GitRepositoryStateService _stateService;

    internal GitRepositoryWorkflowService(GitCommandExecutor executor)
        : this(new GitRepositoryCommandRunner(executor))
    {
    }

    internal GitRepositoryWorkflowService(GitRepositoryCommandRunner runner)
        : this(runner, new GitRepositoryStateService(runner))
    {
    }

    internal GitRepositoryWorkflowService(
        GitRepositoryCommandRunner runner,
        GitRepositoryStateService stateService)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
    }

    public Task ChooseConflictSideAsync(
        Repository repository,
        ConflictFile conflict,
        ConflictResolutionSide side,
        CancellationToken cancellationToken = default)
    {
        ValidateConflictAction(
            conflict,
            side == ConflictResolutionSide.CurrentLocal
                ? conflict.CanChooseCurrentLocal
                : conflict.CanChooseIncomingRemote);

        var operation = GitOperationDetector.Detect(repository);
        var gitSide = side == ConflictResolutionSide.CurrentLocal
            ? operation == RepositoryOperation.Rebase
                ? "--theirs"
                : "--ours"
            : operation == RepositoryOperation.Rebase
                ? "--ours"
                : "--theirs";

        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "checkout",
            gitSide,
            "--",
            conflict.Path);
    }

    public Task KeepConflictDeletionAsync(
        Repository repository,
        ConflictFile conflict,
        CancellationToken cancellationToken = default)
    {
        ValidateConflictAction(conflict, conflict.CanKeepDeletion);
        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "rm",
            "--",
            conflict.Path);
    }

    public Task StageResolvedConflictAsync(
        Repository repository,
        ConflictFile conflict,
        CancellationToken cancellationToken = default)
    {
        ValidateConflictAction(conflict, conflict.CanStage);
        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "add",
            "--",
            conflict.Path);
    }

    public Task ContinueOperationAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        RunOperationCommandAsync(
            repository,
            "continue",
            cancellationToken);

    public Task AbortOperationAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        RunOperationCommandAsync(
            repository,
            "abort",
            cancellationToken);

    public Task SkipOperationAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        RunOperationCommandAsync(
            repository,
            "skip",
            cancellationToken);

    public Task CreateStashAsync(
        Repository repository,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "stash", "push" };

        if (!string.IsNullOrWhiteSpace(message))
        {
            arguments.Add("--message");
            arguments.Add(message.Trim());
        }

        return _runner.RunMutationAsync(
            repository,
            cancellationToken,
            arguments.ToArray());
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

    public async Task<MergeResult> MergeAsync(
        Repository repository,
        string branch,
        CancellationToken cancellationToken = default)
    {
        GitRefValidator.Validate(branch, nameof(branch));

        var before = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD");

        try
        {
            await _runner.RunMutationAsync(
                repository,
                cancellationToken,
                "merge",
                "--no-edit",
                branch);
        }
        catch (RepositoryOpenException exception)
        {
            var state = await _stateService.ReadAsync(
                repository,
                cancellationToken);
            var conflicts =
                state.Operation == RepositoryOperation.Merge
                || state.Changes.Any(change => change.IsConflicted);

            return new MergeResult(
                conflicts
                    ? MergeResultKind.Conflicts
                    : MergeResultKind.Refused,
                exception.Message);
        }

        var after = await _runner.RunOptionalAsync(
            repository.WorkingDirectory,
            cancellationToken,
            "rev-parse",
            "--verify",
            "HEAD");

        if (string.Equals(before, after, StringComparison.Ordinal))
            return new MergeResult(
                MergeResultKind.UpToDate,
                "Git: already up to date.");

        var parents = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            true,
            "show",
            "-s",
            "--format=%P",
            "HEAD");

        return parents.Split(
                   ' ',
                   StringSplitOptions.RemoveEmptyEntries).Length > 1
            ? new MergeResult(
                MergeResultKind.MergeCommit,
                "Git created a merge commit.")
            : new MergeResult(
                MergeResultKind.FastForward,
                "Git completed a fast-forward merge.");
    }

    public async Task<InteractiveRebasePlan> ReadInteractiveRebasePlanAsync(
        Repository repository,
        string onto,
        CancellationToken cancellationToken = default)
    {
        GitRefValidator.Validate(onto, nameof(onto));

        var sourceSnapshot = await ReadRebaseSourceSnapshotAsync(
            repository,
            requireAttachedLocalBranch: false,
            cancellationToken);
        var resolvedOnto = await ResolveCommitAsync(
            repository,
            onto,
            cancellationToken);
        var plan = await BuildInteractiveRebasePlanAsync(
            repository,
            resolvedOnto,
            sourceSnapshot,
            cancellationToken);

        await EnsureRebaseSourceUnchangedAsync(
            repository,
            sourceSnapshot,
            cancellationToken);
        return plan;
    }

    public async Task<InteractiveRebasePlan> ReadInteractiveRebasePlanFromCommitAsync(
        Repository repository,
        string firstCommit,
        CancellationToken cancellationToken = default)
    {
        GitRefValidator.ValidateObjectId(firstCommit, nameof(firstCommit));

        var resolvedFirstCommit = await ResolveCommitAsync(
            repository,
            firstCommit,
            cancellationToken);
        var sourceSnapshot = await ReadRebaseSourceSnapshotAsync(
            repository,
            requireAttachedLocalBranch: true,
            cancellationToken);

        await EnsureCommitIsInCurrentHeadHistoryAsync(
            repository,
            resolvedFirstCommit,
            sourceSnapshot.ExpectedHeadCommit,
            cancellationToken);

        var parentOutput = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "show",
            "-s",
            "--format=%P",
            resolvedFirstCommit);
        var parents = parentOutput.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parents.Length == 0)
            throw new InvalidOperationException(
                "Interactive rebase including the root commit is not supported yet.");

        if (parents.Length != 1)
            throw new InvalidOperationException(
                "Interactive rebase currently supports linear history only.\n\nThe selected range contains merge commits.");

        var plan = await BuildInteractiveRebasePlanAsync(
            repository,
            parents[0],
            sourceSnapshot,
            cancellationToken);

        if (plan.Items.Count == 0
            || !string.Equals(
                plan.Items[0].Commit,
                resolvedFirstCommit,
                StringComparison.Ordinal))
            throw CreateStaleRebasePlanException();

        await EnsureRebaseSourceUnchangedAsync(
            repository,
            sourceSnapshot,
            cancellationToken);
        return plan;
    }


    public async Task<InteractiveRebaseTodo> ReadInteractiveRebaseTodoAsync(
        Repository repository,
        string onto,
        CancellationToken cancellationToken = default)
    {
        var plan = await ReadInteractiveRebasePlanAsync(
            repository,
            onto,
            cancellationToken);
        return ToInteractiveRebaseTodo(plan);
    }

    public async Task<InteractiveRebaseTodo> ReadInteractiveRebaseTodoFromCommitAsync(
        Repository repository,
        string firstCommit,
        CancellationToken cancellationToken = default)
    {
        var plan = await ReadInteractiveRebasePlanFromCommitAsync(
            repository,
            firstCommit,
            cancellationToken);
        return ToInteractiveRebaseTodo(plan);
    }

    public async Task<RebaseResult> StartInteractiveRebaseTodoAsync(
        Repository repository,
        InteractiveRebaseTodo todo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todo);

        EnsureNoActiveOperation(repository);
        GitRefValidator.ValidateObjectId(todo.Onto, nameof(todo.Onto));

        await EnsureRebaseSourceUnchangedAsync(
            repository,
            todo.SourceSnapshot,
            cancellationToken);

        var resolvedOnto = await ResolveCommitAsync(
            repository,
            todo.Onto,
            cancellationToken);
        await EnsureLinearRebaseRangeAsync(
            repository,
            resolvedOnto,
            todo.SourceSnapshot.ExpectedHeadCommit,
            cancellationToken);
        await EnsureRebaseSourceUnchangedAsync(
            repository,
            todo.SourceSnapshot,
            cancellationToken);

        var supportDirectory = Path.Combine(
            repository.GitDirectory,
            "csharpgit-rebase");

        CleanupRebaseSupportDirectory(repository);
        try
        {
            Directory.CreateDirectory(supportDirectory);

            var todoPath = Path.Combine(
                supportDirectory,
                "todo");
            var sequenceEditor = Path.Combine(
                supportDirectory,
                OperatingSystem.IsWindows()
                    ? "sequence-editor.cmd"
                    : "sequence-editor.sh");

            await WriteRawRebaseTodoAsync(
                todoPath,
                todo.TodoText,
                cancellationToken);
            await WriteSequenceEditorAsync(
                sequenceEditor,
                todoPath,
                cancellationToken);
            await WriteRebaseModeAsync(
                supportDirectory,
                RawRebaseMode,
                cancellationToken);

            var result = await RunRebaseCommandAsync(
                repository,
                new Dictionary<string, string?>
                {
                    ["GIT_SEQUENCE_EDITOR"] = QuoteCommand(sequenceEditor)
                },
                cancellationToken,
                "rebase",
                "--interactive",
                resolvedOnto);

            if (GitOperationDetector.Detect(repository)
                != RepositoryOperation.Rebase)
                CleanupRebaseSupportDirectory(repository);

            return result;
        }
        catch
        {
            if (GitOperationDetector.Detect(repository)
                != RepositoryOperation.Rebase)
                CleanupRebaseSupportDirectory(repository);
            throw;
        }
    }

    public async Task<RebaseResult> StartInteractiveRebaseAsync(
        Repository repository,
        InteractiveRebasePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        EnsureNoActiveOperation(repository);
        ValidateRebasePlan(plan);

        if (plan.SourceSnapshot is null)
            throw new InvalidOperationException(
                "The rebase plan does not contain repository source information.\n\nRebuild the plan before starting interactive rebase.");

        await EnsureRebaseSourceUnchangedAsync(
            repository,
            plan.SourceSnapshot,
            cancellationToken);

        var resolvedOnto = await ResolveCommitAsync(
            repository,
            plan.Onto,
            cancellationToken);
        var expected = await BuildInteractiveRebasePlanAsync(
            repository,
            resolvedOnto,
            plan.SourceSnapshot,
            cancellationToken);

        if (!expected.Items
                .Select(item => item.Commit)
                .OrderBy(commit => commit, StringComparer.Ordinal)
                .SequenceEqual(
                    plan.Items
                        .Select(item => item.Commit)
                        .OrderBy(commit => commit, StringComparer.Ordinal),
                    StringComparer.Ordinal))
            throw new InvalidOperationException(
                "The plan must contain every commit in the range exactly once. Use drop to exclude a commit.");

        await EnsureRebaseSourceUnchangedAsync(
            repository,
            plan.SourceSnapshot,
            cancellationToken);

        var supportDirectory = Path.Combine(
            repository.GitDirectory,
            "csharpgit-rebase");

        CleanupRebaseSupportDirectory(repository);
        try
        {
            Directory.CreateDirectory(supportDirectory);

            var todoPath = Path.Combine(
                supportDirectory,
                "todo");
            var sequenceEditor = Path.Combine(
                supportDirectory,
                OperatingSystem.IsWindows()
                    ? "sequence-editor.cmd"
                    : "sequence-editor.sh");
            var messageEditor = Path.Combine(
                supportDirectory,
                OperatingSystem.IsWindows()
                    ? "message-editor.cmd"
                    : "message-editor.sh");

            await File.WriteAllLinesAsync(
                todoPath,
                BuildRebaseTodo(plan, supportDirectory),
                cancellationToken);
            await WriteSequenceEditorAsync(
                sequenceEditor,
                todoPath,
                cancellationToken);
            await WriteNoOpEditorAsync(
                messageEditor,
                cancellationToken);
            await WriteRebaseModeAsync(
                supportDirectory,
                ManagedRebaseMode,
                cancellationToken);

            var environment = new Dictionary<string, string?>
            {
                ["GIT_SEQUENCE_EDITOR"] = QuoteCommand(sequenceEditor),
                ["GIT_EDITOR"] = QuoteCommand(messageEditor)
            };

            var result = await RunRebaseCommandAsync(
                repository,
                environment,
                cancellationToken,
                "rebase",
                "--interactive",
                resolvedOnto);

            if (GitOperationDetector.Detect(repository)
                != RepositoryOperation.Rebase)
                CleanupRebaseSupportDirectory(repository);

            return result;
        }
        catch
        {
            if (GitOperationDetector.Detect(repository)
                != RepositoryOperation.Rebase)
                CleanupRebaseSupportDirectory(repository);
            throw;
        }
    }
    public Task<RebaseResult> ContinueRebaseAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        if (GitOperationDetector.Detect(repository)
            != RepositoryOperation.Rebase)
            throw new InvalidOperationException(
                "No rebase is in progress.");

        return ContinueRebaseCoreAsync(
            repository,
            cancellationToken);
    }

    public async Task AbortRebaseAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        var mode = ReadRebaseMode(repository);
        await _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "rebase",
            "--abort");

        if (IsCSharpGitRebaseMode(mode))
            CleanupRebaseSupportDirectory(repository);
    }

    private async Task<RebaseResult> ContinueRebaseCoreAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        var mode = ReadRebaseMode(repository);
        var environment = new Dictionary<string, string?>();

        if (string.Equals(
                mode,
                ManagedRebaseMode,
                StringComparison.Ordinal))
        {
            var supportDirectory = Path.Combine(
                repository.GitDirectory,
                "csharpgit-rebase");
            Directory.CreateDirectory(supportDirectory);

            var messageEditor = Path.Combine(
                supportDirectory,
                OperatingSystem.IsWindows()
                    ? "message-editor.cmd"
                    : "message-editor.sh");
            await WriteNoOpEditorAsync(
                messageEditor,
                cancellationToken);
            environment["GIT_EDITOR"] = QuoteCommand(messageEditor);
        }

        var result = await RunRebaseCommandAsync(
            repository,
            environment,
            cancellationToken,
            "rebase",
            "--continue");

        if (GitOperationDetector.Detect(repository)
                != RepositoryOperation.Rebase
            && IsCSharpGitRebaseMode(mode))
            CleanupRebaseSupportDirectory(repository);

        return result;
    }

    private async Task<RebaseResult> RunRebaseCommandAsync(
        Repository repository,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        try
        {
            var output = await _runner.RunAsync(
                repository.WorkingDirectory,
                cancellationToken,
                false,
                environment,
                GitCommandKind.User,
                arguments);

            return await ClassifyRebaseResultAsync(
                repository,
                commandSucceeded: true,
                output,
                cancellationToken);
        }
        catch (RepositoryOpenException exception)
        {
            return await ClassifyRebaseResultAsync(
                repository,
                commandSucceeded: false,
                exception.Message,
                cancellationToken);
        }
    }

    private async Task<RebaseResult> ClassifyRebaseResultAsync(
        Repository repository,
        bool commandSucceeded,
        string? diagnostic,
        CancellationToken cancellationToken)
    {
        var state = await _stateService.ReadAsync(
            repository,
            cancellationToken);

        if (state.Operation != RepositoryOperation.Rebase)
        {
            return new RebaseResult(
                commandSucceeded
                    ? RebaseResultKind.Completed
                    : RebaseResultKind.Failed,
                commandSucceeded
                    ? "Interactive rebase completed successfully."
                    : diagnostic ?? "Interactive rebase failed.");
        }

        var hasConflicts = state.Changes.Any(change => change.IsConflicted);
        var kind = hasConflicts
            ? RebaseResultKind.Conflicts
            : RebaseResultKind.Paused;
        var message = !string.IsNullOrWhiteSpace(diagnostic)
            ? diagnostic.Trim()
            : hasConflicts
                ? "Interactive rebase stopped because of conflicts."
                : "Interactive rebase is paused. Complete the requested Git step, then Continue or Abort.";

        return new RebaseResult(kind, message);
    }

    private async Task RunOperationCommandAsync(
        Repository repository,
        string action,
        CancellationToken cancellationToken)
    {
        var state = await _stateService.ReadAsync(
            repository,
            cancellationToken);

        var allowed = action switch
        {
            "continue" => state.CurrentOperation.CanContinue,
            "abort" => state.CurrentOperation.CanAbort,
            "skip" => state.CurrentOperation.CanSkip,
            _ => false
        };

        if (!allowed)
            throw new InvalidOperationException(
                $"The current Git operation does not support {action}.");

        var command = state.Operation switch
        {
            RepositoryOperation.Merge => "merge",
            RepositoryOperation.Rebase => "rebase",
            RepositoryOperation.CherryPick => "cherry-pick",
            RepositoryOperation.Revert => "revert",
            _ => throw new InvalidOperationException(
                "No supported Git operation is in progress.")
        };

        if (state.Operation == RepositoryOperation.Rebase
            && action == "continue")
        {
            var result = await ContinueRebaseCoreAsync(
                repository,
                cancellationToken);
            if (result.Kind == RebaseResultKind.Failed)
                throw new InvalidOperationException(result.Message);
            return;
        }

        if (state.Operation == RepositoryOperation.Rebase
            && action == "abort")
        {
            await AbortRebaseAsync(
                repository,
                cancellationToken);
            return;
        }

        if (action == "continue"
            && state.Operation is RepositoryOperation.Merge
                or RepositoryOperation.CherryPick
                or RepositoryOperation.Revert)
        {
            var supportDirectory = Path.Combine(
                repository.GitDirectory,
                "csharpgit-merge");
            Directory.CreateDirectory(supportDirectory);

            var messageEditor = Path.Combine(
                supportDirectory,
                OperatingSystem.IsWindows()
                    ? "message-editor.cmd"
                    : "message-editor.sh");
            await WriteNoOpEditorAsync(
                messageEditor,
                cancellationToken);

            await _runner.RunAsync(
                repository.WorkingDirectory,
                cancellationToken,
                false,
                new Dictionary<string, string?>
                {
                    ["GIT_EDITOR"] = QuoteCommand(messageEditor)
                },
                GitCommandKind.User,
                command,
                $"--{action}");
            return;
        }

        await _runner.RunMutationAsync(
            repository,
            cancellationToken,
            command,
            $"--{action}");
    }

    private async Task<InteractiveRebasePlan> BuildInteractiveRebasePlanAsync(
        Repository repository,
        string resolvedOnto,
        InteractiveRebaseSourceSnapshot sourceSnapshot,
        CancellationToken cancellationToken)
    {
        await EnsureLinearRebaseRangeAsync(
            repository,
            resolvedOnto,
            sourceSnapshot.ExpectedHeadCommit,
            cancellationToken);

        var output = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "log",
            "--reverse",
            "--format=%H%x00%s%x1e",
            $"{resolvedOnto}..{sourceSnapshot.ExpectedHeadCommit}");

        var items = output.Split(
                '\x1e',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(record =>
                record.TrimStart('\r', '\n').Split('\0', 2))
            .Where(fields => fields.Length == 2)
            .Select(fields =>
                new RebasePlanItem(
                    fields[0],
                    fields[1].TrimEnd('\r', '\n'),
                    RebaseAction.Pick))
            .ToList();

        return new InteractiveRebasePlan(
            resolvedOnto,
            items,
            sourceSnapshot);
    }

    private async Task<InteractiveRebaseSourceSnapshot> ReadRebaseSourceSnapshotAsync(
        Repository repository,
        bool requireAttachedLocalBranch,
        CancellationToken cancellationToken)
    {
        EnsureNoActiveOperation(repository);

        var headCommit = await ResolveCommitAsync(
            repository,
            "HEAD",
            cancellationToken);
        var symbolicHead = NormalizeSymbolicHead(
            await _runner.RunOptionalAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "symbolic-ref",
                "-q",
                "HEAD"));

        if (requireAttachedLocalBranch
            && (symbolicHead is null
                || !symbolicHead.StartsWith(
                    "refs/heads/",
                    StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Interactive rebase from History requires a local branch to be checked out.");

        return new InteractiveRebaseSourceSnapshot(
            headCommit,
            symbolicHead);
    }

    private async Task EnsureRebaseSourceUnchangedAsync(
        Repository repository,
        InteractiveRebaseSourceSnapshot expected,
        CancellationToken cancellationToken)
    {
        EnsureNoActiveOperation(repository);

        var actualHeadCommit = await ResolveCommitAsync(
            repository,
            "HEAD",
            cancellationToken);
        var actualHeadReference = NormalizeSymbolicHead(
            await _runner.RunOptionalAsync(
                repository.WorkingDirectory,
                cancellationToken,
                "symbolic-ref",
                "-q",
                "HEAD"));

        if (!string.Equals(
                expected.ExpectedHeadCommit,
                actualHeadCommit,
                StringComparison.Ordinal)
            || !string.Equals(
                expected.ExpectedHeadReference,
                actualHeadReference,
                StringComparison.Ordinal))
            throw CreateStaleRebasePlanException();
    }

    private async Task EnsureCommitIsInCurrentHeadHistoryAsync(
        Repository repository,
        string commit,
        string headCommit,
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunForResultAsync(
            repository.WorkingDirectory,
            "Repository",
            GitCommandKind.Internal,
            cancellationToken,
            null,
            ["merge-base", "--is-ancestor", commit, headCommit]);

        if (result.ExitCode == 0) return;

        if (result.ExitCode == 1)
            throw new InvalidOperationException(
                "This commit is not part of the current branch history.\n\nSwitch to a branch containing this commit before starting interactive rebase.");

        throw GitRepositoryCommandRunner.CreateCommandFailure(result);
    }

    private async Task EnsureLinearRebaseRangeAsync(
        Repository repository,
        string resolvedOnto,
        string headCommit,
        CancellationToken cancellationToken)
    {
        var merges = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "rev-list",
            "--merges",
            $"{resolvedOnto}..{headCommit}");

        if (!string.IsNullOrWhiteSpace(merges))
            throw new InvalidOperationException(
                "Interactive rebase currently supports linear history only.\n\nThe selected range contains merge commits.");
    }

    private async Task<string> ResolveCommitAsync(
        Repository repository,
        string value,
        CancellationToken cancellationToken) =>
        (await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            true,
            "rev-parse",
            "--verify",
            $"{value}^{{commit}}")).Trim();

    private static string? NormalizeSymbolicHead(string value)
    {
        var normalized = value.Trim();
        return normalized.Length == 0
            ? null
            : normalized;
    }

    private static void EnsureNoActiveOperation(Repository repository)
    {
        if (GitOperationDetector.Detect(repository) != RepositoryOperation.None)
            throw new InvalidOperationException(
                "Complete or abort the current Git operation first.");
    }

    private static InvalidOperationException CreateStaleRebasePlanException() =>
        new(
            "Repository history changed after the rebase plan was built.\n\nRebuild the plan before starting interactive rebase.");
    private static void ValidateRebasePlan(
        InteractiveRebasePlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Onto)
            || plan.Items.Count == 0)
            throw new ArgumentException(
                "The rebase plan is empty.",
                nameof(plan));

        if (plan.Items[0].Action
            is RebaseAction.Squash or RebaseAction.Fixup)
            throw new ArgumentException(
                "The first commit cannot use squash or fixup.",
                nameof(plan));

        if (plan.Items
                .Select(item => item.Commit)
                .Distinct(StringComparer.Ordinal)
                .Count()
            != plan.Items.Count)
            throw new ArgumentException(
                "Commits in the plan must not be duplicated.",
                nameof(plan));

        foreach (var item in plan.Items)
        {
            GitRefValidator.ValidateObjectId(
                item.Commit,
                nameof(item.Commit));

            if (item.Action == RebaseAction.Reword
                && string.IsNullOrWhiteSpace(item.NewMessage))
                throw new ArgumentException(
                    "Reword requires a non-empty new message.",
                    nameof(plan));
        }
    }

    private static IEnumerable<string> BuildRebaseTodo(
        InteractiveRebasePlan plan,
        string supportDirectory)
    {
        var index = 0;

        foreach (var item in plan.Items)
        {
            if (item.Action == RebaseAction.Reword)
            {
                var rewordIndex = index++;
                var messagePath = Path.Combine(
                    supportDirectory,
                    $"message-{rewordIndex}.txt");
                var markerPath = Path.Combine(
                    supportDirectory,
                    $"reword-{rewordIndex}.sha");

                File.WriteAllText(
                    messagePath,
                    item.NewMessage!);

                yield return
                    $"pick {item.Commit} {item.Subject}";
                yield return
                    $"exec git commit --amend --no-verify --no-gpg-sign -F {QuoteTodoPath(messagePath)}";
                yield return
                    $"exec git rev-parse HEAD > {QuoteTodoPath(markerPath)}";
            }
            else
            {
                yield return
                    $"{item.Action.ToString().ToLowerInvariant()} {item.Commit} {item.Subject}";
            }
        }
    }

    private static InteractiveRebaseTodo ToInteractiveRebaseTodo(
        InteractiveRebasePlan plan)
    {
        if (plan.SourceSnapshot is null)
            throw new InvalidOperationException(
                "The rebase plan does not contain repository source information.");

        var todoText = string.Join(
            Environment.NewLine,
            plan.Items.Select(item =>
                $"pick {item.Commit} {item.Subject}"));

        if (todoText.Length > 0)
            todoText += Environment.NewLine;

        return new InteractiveRebaseTodo(
            plan.Onto,
            todoText,
            plan.SourceSnapshot);
    }

    private static async Task WriteRawRebaseTodoAsync(
        string todoPath,
        string todoText,
        CancellationToken cancellationToken)
    {
        var content = todoText;
        if (content.Length > 0
            && !content.EndsWith('\n')
            && !content.EndsWith('\r'))
            content += Environment.NewLine;

        await File.WriteAllTextAsync(
            todoPath,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    private static async Task WriteRebaseModeAsync(
        string supportDirectory,
        string mode,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(
                supportDirectory,
                RebaseModeFileName),
            mode + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    private static string? ReadRebaseMode(Repository repository)
    {
        var path = Path.Combine(
            repository.GitDirectory,
            "csharpgit-rebase",
            RebaseModeFileName);
        if (!File.Exists(path))
            return null;

        try
        {
            var mode = File.ReadAllText(path).Trim();
            return IsCSharpGitRebaseMode(mode)
                ? mode
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsCSharpGitRebaseMode(string? mode) =>
        string.Equals(
            mode,
            ManagedRebaseMode,
            StringComparison.Ordinal)
        || string.Equals(
            mode,
            RawRebaseMode,
            StringComparison.Ordinal);

    private static async Task WriteSequenceEditorAsync(
        string scriptPath,
        string todoPath,
        CancellationToken cancellationToken)
    {
        var content = OperatingSystem.IsWindows()
            ? $"@echo off{Environment.NewLine}copy /Y \"{todoPath}\" \"%~1\" >nul{Environment.NewLine}"
            : $"#!/bin/sh{Environment.NewLine}cp -- {ShellQuote(todoPath)} \"$1\"{Environment.NewLine}";

        await File.WriteAllTextAsync(
            scriptPath,
            content,
            cancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
    }

    private static async Task WriteNoOpEditorAsync(
        string scriptPath,
        CancellationToken cancellationToken)
    {
        var content = OperatingSystem.IsWindows()
            ? $"@echo off{Environment.NewLine}exit /b 0{Environment.NewLine}"
            : $"#!/bin/sh{Environment.NewLine}exit 0{Environment.NewLine}";

        await File.WriteAllTextAsync(
            scriptPath,
            content,
            cancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
    }

    private static void CleanupRebaseSupportDirectory(
        Repository repository)
    {
        var path = Path.Combine(
            repository.GitDirectory,
            "csharpgit-rebase");

        if (!Directory.Exists(path)) return;

        try
        {
            Directory.Delete(path, recursive: true);
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

    private static void ValidateConflictAction(
        ConflictFile conflict,
        bool allowed)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        GitPathValidator.ValidateRepositoryRelative(conflict.Path);

        if (!allowed)
            throw new InvalidOperationException(
                "This action is unavailable for the selected conflict type or state.");
    }

    private static string QuoteCommand(string path) =>
        $"\"{path.Replace("\"", "\\\"")}\"";

    private static string QuoteTodoPath(string path) =>
        $"\"{path.Replace("\\", "/").Replace("\"", "\\\"")}\"";

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";
}
