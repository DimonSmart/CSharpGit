using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitRepositoryWorkflowService : IRepositoryWorkflowService
{
    internal GitRepositoryWorkflowService()
        : this(GitCommandExecutor.Default)
    {
    }


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

        var resolvedOnto = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            true,
            "rev-parse",
            "--verify",
            $"{onto}^{{commit}}");
        var output = await _runner.RunAsync(
            repository.WorkingDirectory,
            cancellationToken,
            false,
            "log",
            "--reverse",
            "--format=%H%x00%s%x1e",
            $"{resolvedOnto}..HEAD");

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

        return new InteractiveRebasePlan(resolvedOnto, items);
    }

    public async Task<RebaseResult> StartInteractiveRebaseAsync(
        Repository repository,
        InteractiveRebasePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (GitOperationDetector.Detect(repository) != RepositoryOperation.None)
            throw new InvalidOperationException(
                "Complete or abort the current Git operation first.");

        ValidateRebasePlan(plan);

        var expected = await ReadInteractiveRebasePlanAsync(
            repository,
            plan.Onto,
            cancellationToken);
        if (!expected.Items
                .Select(item => item.Commit)
                .Order()
                .SequenceEqual(
                    plan.Items
                        .Select(item => item.Commit)
                        .Order(),
                    StringComparer.Ordinal))
            throw new InvalidOperationException(
                "The plan must contain every commit in the range exactly once. Use drop to exclude a commit.");

        var supportDirectory = Path.Combine(
            repository.GitDirectory,
            "csharpgit-rebase");

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
                plan.Onto);

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
        await _runner.RunMutationAsync(
            repository,
            cancellationToken,
            "rebase",
            "--abort");
        CleanupRebaseSupportDirectory(repository);
    }

    private async Task<RebaseResult> ContinueRebaseCoreAsync(
        Repository repository,
        CancellationToken cancellationToken)
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

        var environment = new Dictionary<string, string?>
        {
            ["GIT_EDITOR"] = QuoteCommand(messageEditor)
        };

        var result = await RunRebaseCommandAsync(
            repository,
            environment,
            cancellationToken,
            "rebase",
            "--continue");

        if (GitOperationDetector.Detect(repository)
            != RepositoryOperation.Rebase)
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
            await _runner.RunAsync(
                repository.WorkingDirectory,
                cancellationToken,
                false,
                environment,
                GitCommandKind.User,
                arguments);

            return new RebaseResult(
                RebaseResultKind.Completed,
                "Interactive rebase completed successfully.");
        }
        catch (RepositoryOpenException exception)
        {
            var state = await _stateService.ReadAsync(
                repository,
                cancellationToken);

            return new RebaseResult(
                state.Operation == RepositoryOperation.Rebase
                && state.Changes.Any(change => change.IsConflicted)
                    ? RebaseResultKind.Conflicts
                    : RebaseResultKind.Failed,
                exception.Message);
        }
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
