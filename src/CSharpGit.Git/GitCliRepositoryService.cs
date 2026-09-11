using System.Diagnostics;
using System.Text;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed partial class GitCliRepositoryService : IRepositoryService, IRepositoryStateService, IWorkingTreeService, IReferenceService, IRepositoryWorkflowService
{
    private readonly GitCommandExecutor _executor;

    public GitCliRepositoryService() : this(GitCommandExecutor.Default) { }

    internal GitCliRepositoryService(GitCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task<Repository> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new RepositoryOpenException("The selected folder does not exist.");

        try
        {
            await EnsureGitAvailableAsync(cancellationToken);
            var root = await RunGitAsync(path, cancellationToken, true, "rev-parse", "--show-toplevel");
            var gitDirectory = await RunGitAsync(path, cancellationToken, true, "rev-parse", "--absolute-git-dir");
            var commonDirectory = await RunGitAsync(path, cancellationToken, true, "rev-parse", "--path-format=absolute", "--git-common-dir");

            return new Repository(
                Path.GetFullPath(path),
                Path.GetFullPath(root),
                Path.GetFullPath(gitDirectory),
                !PathsEqual(gitDirectory, commonDirectory));
        }
        catch (RepositoryOpenException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RepositoryOpenException("Git could not be started. Make sure Git is installed and available through PATH.", exception);
        }
    }

    public async Task<RepositoryState> ReadAsync(Repository repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        await EnsureGitAvailableAsync(cancellationToken);

        var headReference = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "symbolic-ref", "--quiet", "--short", "HEAD");
        var headCommit = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "rev-parse", "--verify", "HEAD");
        var status = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        var globalConfig = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "config", "--global", "--null", "--list");
        var localConfig = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "config", "--local", "--null", "--list");
        var references = await ReadReferencesAsync(repository, headReference, cancellationToken);
        var stashes = await ReadStashesAsync(repository, cancellationToken);

        var operation = DetectOperation(repository);
        var operationState = await ReadOperationStateAsync(repository, operation, status, cancellationToken);
        return new RepositoryState(
            repository,
            EmptyToNull(headReference),
            EmptyToNull(headCommit),
            string.IsNullOrWhiteSpace(headReference),
            operation,
            ParseStatus(status),
            ParseConfiguration(globalConfig),
            ParseConfiguration(localConfig),
            DateTimeOffset.UtcNow,
            references,
            stashes,
            operationState);
    }

    public Task ChooseConflictSideAsync(Repository repository, ConflictFile conflict, ConflictResolutionSide side, CancellationToken cancellationToken = default)
    {
        ValidateConflictAction(conflict, side == ConflictResolutionSide.CurrentLocal ? conflict.CanChooseCurrentLocal : conflict.CanChooseIncomingRemote);
        var operation = DetectOperation(repository);
        var gitSide = side == ConflictResolutionSide.CurrentLocal
            ? (operation == RepositoryOperation.Rebase ? "--theirs" : "--ours")
            : (operation == RepositoryOperation.Rebase ? "--ours" : "--theirs");
        return RunGitForMutationAsync(repository, cancellationToken, "checkout", gitSide, "--", conflict.Path);
    }

    public Task KeepConflictDeletionAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default)
    {
        ValidateConflictAction(conflict, conflict.CanKeepDeletion);
        return RunGitForMutationAsync(repository, cancellationToken, "rm", "--", conflict.Path);
    }

    public Task StageResolvedConflictAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default)
    {
        ValidateConflictAction(conflict, conflict.CanStage);
        return RunGitForMutationAsync(repository, cancellationToken, "add", "--", conflict.Path);
    }

    public async Task ConfigureMergeToolAsync(Repository repository, MergeToolConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var name = configuration.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException("The merge tool name may contain only letters, digits, '-' and '_'.", nameof(configuration));
        if (configuration.Kind == MergeToolConfigurationKind.Preset && !MergeToolPresets.Known.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Unknown merge tool preset.", nameof(configuration));

        var scope = configuration.Scope == GitConfigurationScope.Global ? "--global" : "--local";
        string? command = null;
        if (configuration.Kind != MergeToolConfigurationKind.Preset)
        {
            if (string.IsNullOrWhiteSpace(configuration.ExecutablePath))
                throw new ArgumentException("A custom merge tool requires an executable/path or a complete command.", nameof(configuration));
            command = configuration.Kind == MergeToolConfigurationKind.CustomExecutable
                ? $"{ShellQuote(configuration.ExecutablePath.Trim())} {BuildMergeToolArguments(configuration.CommandArguments)}"
                : $"{configuration.ExecutablePath.Trim()} {configuration.CommandArguments}".Trim();
            EnsureMergeToolVariables(command);
        }

        await RunGitForMutationAsync(repository, cancellationToken, "config", scope, "merge.tool", name);
        await RunGitForMutationAsync(repository, cancellationToken, "config", scope, "mergetool.prompt", "false");

        if (configuration.Kind == MergeToolConfigurationKind.Preset)
        {
            await UnsetConfigurationAsync(repository, scope, $"mergetool.{name}.cmd", cancellationToken);
            await UnsetConfigurationAsync(repository, scope, $"mergetool.{name}.trustExitCode", cancellationToken);
            if (!string.IsNullOrWhiteSpace(configuration.ExecutablePath))
                await RunGitForMutationAsync(repository, cancellationToken, "config", scope, $"mergetool.{name}.path", configuration.ExecutablePath.Trim());
            else
                await UnsetConfigurationAsync(repository, scope, $"mergetool.{name}.path", cancellationToken);
            return;
        }

        await UnsetConfigurationAsync(repository, scope, $"mergetool.{name}.path", cancellationToken);
        await RunGitForMutationAsync(repository, cancellationToken, "config", scope, $"mergetool.{name}.cmd", command!);
        await RunGitForMutationAsync(repository, cancellationToken, "config", scope, $"mergetool.{name}.trustExitCode", "true");
    }

    public async Task RunMergeToolForFileAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default)
    {
        ValidateConflictAction(conflict, conflict.CanRunMergeTool);
        await RunMergeToolAsync(repository, cancellationToken, "mergetool", "--no-prompt", "--", conflict.Path);
    }

    public Task RunMergeToolWorkflowAsync(Repository repository, CancellationToken cancellationToken = default) =>
        RunMergeToolAsync(repository, cancellationToken, "mergetool", "--no-prompt");

    public async Task OpenConflictAsync(Repository repository, ConflictFile conflict, CancellationToken cancellationToken = default)
    {
        ValidateConflictAction(conflict, conflict.CanOpenManually);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(Path.Combine(repository.WorkingDirectory, conflict.Path));
        if (!fullPath.StartsWith(Path.GetFullPath(repository.WorkingDirectory) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("The conflict path is outside the repository.");
        Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
        await Task.CompletedTask;
    }

    public Task ContinueOperationAsync(Repository repository, CancellationToken cancellationToken = default) =>
        RunOperationCommandAsync(repository, "continue", cancellationToken);

    public Task AbortOperationAsync(Repository repository, CancellationToken cancellationToken = default) =>
        RunOperationCommandAsync(repository, "abort", cancellationToken);

    public Task SkipOperationAsync(Repository repository, CancellationToken cancellationToken = default) =>
        RunOperationCommandAsync(repository, "skip", cancellationToken);

    public Task CreateStashAsync(Repository repository, string? message = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "stash", "push" };
        if (!string.IsNullOrWhiteSpace(message))
        {
            arguments.Add("--message");
            arguments.Add(message.Trim());
        }
        return RunGitForMutationAsync(repository, cancellationToken, arguments.ToArray());
    }

    public Task ApplyStashAsync(Repository repository, string stashName, CancellationToken cancellationToken = default)
    {
        ValidateStashName(stashName);
        return RunGitForMutationAsync(repository, cancellationToken, "stash", "apply", stashName);
    }

    public Task PopStashAsync(Repository repository, string stashName, CancellationToken cancellationToken = default)
    {
        ValidateStashName(stashName);
        return RunGitForMutationAsync(repository, cancellationToken, "stash", "pop", stashName);
    }

    public async Task<MergeResult> MergeAsync(Repository repository, string branch, CancellationToken cancellationToken = default)
    {
        ValidateRefName(branch, nameof(branch));
        var before = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "rev-parse", "--verify", "HEAD");
        try
        {
            await RunGitForMutationAsync(repository, cancellationToken, "merge", "--no-edit", branch);
        }
        catch (RepositoryOpenException exception)
        {
            var status = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "status", "--porcelain=v1", "-z", "--untracked-files=all");
            var conflicts = File.Exists(Path.Combine(repository.GitDirectory, "MERGE_HEAD")) || ParseStatus(status).Any(change => change.IsConflicted);
            return new MergeResult(conflicts ? MergeResultKind.Conflicts : MergeResultKind.Refused, exception.Message);
        }

        var after = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "rev-parse", "--verify", "HEAD");
        if (string.Equals(before, after, StringComparison.Ordinal)) return new MergeResult(MergeResultKind.UpToDate, "Git: already up to date.");
        var parents = await RunGitAsync(repository.WorkingDirectory, cancellationToken, true, "show", "-s", "--format=%P", "HEAD");
        return parents.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1
            ? new MergeResult(MergeResultKind.MergeCommit, "Git created a merge commit.")
            : new MergeResult(MergeResultKind.FastForward, "Git completed a fast-forward merge.");
    }

    public async Task<InteractiveRebasePlan> ReadInteractiveRebasePlanAsync(Repository repository, string onto, CancellationToken cancellationToken = default)
    {
        ValidateRefName(onto, nameof(onto));
        var resolvedOnto = await RunGitAsync(repository.WorkingDirectory, cancellationToken, true, "rev-parse", "--verify", $"{onto}^{{commit}}");
        var output = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "log", "--reverse", "--format=%H%x00%s%x1e", $"{resolvedOnto}..HEAD");
        var items = output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries).Select(record => record.TrimStart('\r', '\n').Split('\0', 2))
            .Where(fields => fields.Length == 2).Select(fields => new RebasePlanItem(fields[0], fields[1].TrimEnd('\r', '\n'), RebaseAction.Pick)).ToList();
        return new InteractiveRebasePlan(resolvedOnto, items);
    }

    public async Task<RebaseResult> StartInteractiveRebaseAsync(Repository repository, InteractiveRebasePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (DetectOperation(repository) != RepositoryOperation.None) throw new InvalidOperationException("Complete or abort the current Git operation first.");
        ValidateRebasePlan(plan);
        var expected = await ReadInteractiveRebasePlanAsync(repository, plan.Onto, cancellationToken);
        if (!expected.Items.Select(item => item.Commit).Order().SequenceEqual(plan.Items.Select(item => item.Commit).Order(), StringComparer.Ordinal))
            throw new InvalidOperationException("The plan must contain every commit in the range exactly once. Use drop to exclude a commit.");

        var supportDirectory = Path.Combine(repository.GitDirectory, "csharpgit-rebase");
        Directory.CreateDirectory(supportDirectory);
        var todoPath = Path.Combine(supportDirectory, "todo");
        var sequenceEditor = Path.Combine(supportDirectory, OperatingSystem.IsWindows() ? "sequence-editor.cmd" : "sequence-editor.sh");
        var messageEditor = Path.Combine(supportDirectory, OperatingSystem.IsWindows() ? "message-editor.cmd" : "message-editor.sh");
        await File.WriteAllLinesAsync(todoPath, BuildRebaseTodo(plan, supportDirectory), cancellationToken);
        await WriteSequenceEditorAsync(sequenceEditor, todoPath, cancellationToken);
        await WriteNoOpEditorAsync(messageEditor, cancellationToken);
        var environment = new Dictionary<string, string?>
        {
            ["GIT_SEQUENCE_EDITOR"] = QuoteCommand(sequenceEditor),
            ["GIT_EDITOR"] = QuoteCommand(messageEditor)
        };
        return await RunRebaseCommandAsync(repository, environment, cancellationToken, "rebase", "--interactive", plan.Onto);
    }

    public Task<RebaseResult> ContinueRebaseAsync(Repository repository, CancellationToken cancellationToken = default)
    {
        if (DetectOperation(repository) != RepositoryOperation.Rebase) throw new InvalidOperationException("No rebase is in progress.");
        return ContinueRebaseCoreAsync(repository, cancellationToken);
    }

    private async Task<RebaseResult> ContinueRebaseCoreAsync(Repository repository, CancellationToken cancellationToken)
    {
        var supportDirectory = Path.Combine(repository.GitDirectory, "csharpgit-rebase");
        Directory.CreateDirectory(supportDirectory);
        var messageEditor = Path.Combine(supportDirectory, OperatingSystem.IsWindows() ? "message-editor.cmd" : "message-editor.sh");
        await WriteNoOpEditorAsync(messageEditor, cancellationToken);
        var environment = new Dictionary<string, string?> { ["GIT_EDITOR"] = QuoteCommand(messageEditor) };
        return await RunRebaseCommandAsync(repository, environment, cancellationToken, "rebase", "--continue");
    }

    public Task AbortRebaseAsync(Repository repository, CancellationToken cancellationToken = default) =>
        RunGitForMutationAsync(repository, cancellationToken, "rebase", "--abort");

    private async Task<RebaseResult> RunRebaseCommandAsync(Repository repository, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken, params string[] arguments)
    {
        try
        {
            await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, environment, GitCommandKind.User, arguments);
            return new RebaseResult(RebaseResultKind.Completed, "Interactive rebase completed successfully.");
        }
        catch (RepositoryOpenException exception)
        {
            var state = await ReadAsync(repository, cancellationToken);
            return new RebaseResult(state.Operation == RepositoryOperation.Rebase && state.Changes.Any(change => change.IsConflicted)
                ? RebaseResultKind.Conflicts : RebaseResultKind.Failed, exception.Message);
        }
    }

    private static void ValidateRebasePlan(InteractiveRebasePlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Onto) || plan.Items.Count == 0) throw new ArgumentException("The rebase plan is empty.", nameof(plan));
        if (plan.Items[0].Action is RebaseAction.Squash or RebaseAction.Fixup) throw new ArgumentException("The first commit cannot use squash or fixup.", nameof(plan));
        if (plan.Items.Select(item => item.Commit).Distinct(StringComparer.Ordinal).Count() != plan.Items.Count) throw new ArgumentException("Commits in the plan must not be duplicated.", nameof(plan));
        foreach (var item in plan.Items)
        {
            ValidateObjectName(item.Commit);
            if (item.Action == RebaseAction.Reword && string.IsNullOrWhiteSpace(item.NewMessage)) throw new ArgumentException("Reword requires a non-empty new message.", nameof(plan));
        }
    }

    private static IEnumerable<string> BuildRebaseTodo(InteractiveRebasePlan plan, string supportDirectory)
    {
        var index = 0;
        foreach (var item in plan.Items)
        {
            if (item.Action == RebaseAction.Reword)
            {
                var messagePath = Path.Combine(supportDirectory, $"message-{index++}.txt");
                File.WriteAllText(messagePath, item.NewMessage!.Trim() + Environment.NewLine);
                yield return $"pick {item.Commit} {item.Subject}";
                yield return $"exec git commit --amend --no-verify -F {QuoteTodoPath(messagePath)}";
            }
            else yield return $"{item.Action.ToString().ToLowerInvariant()} {item.Commit} {item.Subject}";
        }
    }

    private static async Task WriteSequenceEditorAsync(string scriptPath, string todoPath, CancellationToken cancellationToken)
    {
        var content = OperatingSystem.IsWindows()
            ? $"@echo off{Environment.NewLine}copy /Y \"{todoPath}\" \"%~1\" >nul{Environment.NewLine}"
            : $"#!/bin/sh{Environment.NewLine}cp -- {ShellQuote(todoPath)} \"$1\"{Environment.NewLine}";
        await File.WriteAllTextAsync(scriptPath, content, cancellationToken);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static async Task WriteNoOpEditorAsync(string scriptPath, CancellationToken cancellationToken)
    {
        var content = OperatingSystem.IsWindows()
            ? $"@echo off{Environment.NewLine}exit /b 0{Environment.NewLine}"
            : $"#!/bin/sh{Environment.NewLine}exit 0{Environment.NewLine}";
        await File.WriteAllTextAsync(scriptPath, content, cancellationToken);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static string QuoteCommand(string path) => $"\"{path.Replace("\"", "\\\"")}\"";
    private static string QuoteTodoPath(string path) => $"\"{path.Replace("\\", "/").Replace("\"", "\\\"")}\"";
    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private async Task<IReadOnlyList<GitStash>> ReadStashesAsync(Repository repository, CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "stash", "list", "--format=%gd%x00%H%x00%gs%x1e");
        var result = new List<GitStash>();
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\r', '\n').Split('\0', 3);
            if (fields.Length == 3) result.Add(new GitStash(fields[0], fields[1], fields[2].TrimEnd('\r', '\n')));
        }
        return result;
    }

    private static void ValidateStashName(string value)
    {
        if (!value.StartsWith("stash@{", StringComparison.Ordinal) || !value.EndsWith('}') ||
            !int.TryParse(value.AsSpan(7, value.Length - 8), out var index) || index < 0)
            throw new ArgumentException("Invalid stash reference.", nameof(value));
    }

    public Task SwitchBranchAsync(Repository repository, string branch, CancellationToken cancellationToken = default)
    {
        ValidateRefName(branch, nameof(branch));
        return RunGitForMutationAsync(repository, cancellationToken, "switch", branch);
    }

    public Task CheckoutAsync(Repository repository, string reference, CancellationToken cancellationToken = default)
    {
        ValidateRefName(reference, nameof(reference));
        return RunGitForMutationAsync(repository, cancellationToken, "checkout", "--detach", reference);
    }

    public Task CreateBranchAsync(Repository repository, string branch, string? startPoint = null, bool switchToBranch = true, CancellationToken cancellationToken = default)
    {
        ValidateRefName(branch, nameof(branch));
        if (startPoint is not null) ValidateRefName(startPoint, nameof(startPoint));
        var arguments = new List<string> { "switch", switchToBranch ? "-c" : "--no-track" };
        if (!switchToBranch) return RunGitForMutationAsync(repository, cancellationToken, "branch", branch, startPoint ?? "HEAD");
        arguments.Add(branch);
        if (startPoint is not null) arguments.Add(startPoint);
        return RunGitForMutationAsync(repository, cancellationToken, arguments.ToArray());
    }

    public Task DeleteBranchAsync(Repository repository, string branch, CancellationToken cancellationToken = default)
    {
        ValidateRefName(branch, nameof(branch));
        return RunGitForMutationAsync(repository, cancellationToken, "branch", "--delete", branch);
    }

    public Task CheckoutRemoteBranchAsync(Repository repository, string remoteBranch, string localBranch, CancellationToken cancellationToken = default)
    {
        ValidateRefName(remoteBranch, nameof(remoteBranch));
        ValidateRefName(localBranch, nameof(localBranch));
        return RunGitForMutationAsync(repository, cancellationToken, "switch", "--create", localBranch, "--track", remoteBranch);
    }

    public Task FetchAsync(Repository repository, string remote, CancellationToken cancellationToken = default)
    {
        ValidateRefName(remote, nameof(remote));
        return RunGitForMutationAsync(repository, cancellationToken, "fetch", remote);
    }

    public Task FetchAllAsync(Repository repository, CancellationToken cancellationToken = default) =>
        RunGitForMutationAsync(repository, cancellationToken, "fetch", "--all");

    public async Task PullAsync(Repository repository, CancellationToken cancellationToken = default)
    {
        var branch = await CurrentBranchAsync(repository, cancellationToken);
        var upstream = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}");
        if (string.IsNullOrWhiteSpace(upstream))
            throw new InvalidOperationException($"Branch '{branch}' has no configured upstream. Explicitly select a remote and branch for push, then optionally set the upstream.");
        await RunGitForMutationAsync(repository, cancellationToken, "pull");
    }

    public async Task PushAsync(Repository repository, string? remote = null, string? branch = null, bool setUpstream = false, CancellationToken cancellationToken = default)
    {
        var current = await CurrentBranchAsync(repository, cancellationToken);
        var upstream = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}");
        if (!string.IsNullOrWhiteSpace(upstream) && remote is null && branch is null)
        {
            await RunPushAsync(repository, cancellationToken, "push", "--porcelain");
            return;
        }
        if (string.IsNullOrWhiteSpace(remote) || string.IsNullOrWhiteSpace(branch))
            throw new InvalidOperationException("No upstream is configured. Explicitly select a remote and remote branch name; Git GUI does not select them automatically.");
        ValidateRefName(remote, nameof(remote));
        ValidateRefName(branch, nameof(branch));
        var refspec = $"{current}:refs/heads/{branch}";
        await RunPushAsync(repository, cancellationToken,
            setUpstream
                ? ["push", "--porcelain", "--set-upstream", remote, refspec]
                : ["push", "--porcelain", remote, refspec]);
    }

    private async Task<GitReferences> ReadReferencesAsync(Repository repository, string currentBranch, CancellationToken cancellationToken)
    {
        const string format = "%(refname)%00%(objectname)%00%(*objectname)%00%(upstream:short)%00%(upstream:track)%1e";
        var output = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "for-each-ref", $"--format={format}", "refs/heads", "refs/remotes", "refs/tags");
        var local = new List<GitBranch>();
        var remote = new List<GitBranch>();
        var tags = new List<GitTag>();
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\r', '\n').Split('\0');
            if (fields.Length < 5) continue;
            var fullName = fields[0];
            if (fullName.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                var name = fullName[11..];
                var (ahead, behind) = ParseTracking(fields[4]);
                local.Add(new GitBranch(name, fields[1], string.Equals(name, currentBranch, StringComparison.Ordinal), EmptyToNull(fields[3]), ahead, behind));
            }
            else if (fullName.StartsWith("refs/remotes/", StringComparison.Ordinal) && !fullName.EndsWith("/HEAD", StringComparison.Ordinal))
                remote.Add(new GitBranch(fullName[13..], fields[1]));
            else if (fullName.StartsWith("refs/tags/", StringComparison.Ordinal))
                tags.Add(new GitTag(fullName[10..], string.IsNullOrEmpty(fields[2]) ? fields[1] : fields[2]));
        }

        var remotes = new List<GitRemote>();
        var remoteNames = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "remote");
        foreach (var name in remoteNames.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fetchUrl = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "remote", "get-url", name);
            var pushUrl = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "remote", "get-url", "--push", name);
            remotes.Add(new GitRemote(name, fetchUrl, pushUrl));
        }
        return new GitReferences(local, remote, remotes, tags);
    }

    private async Task<string> CurrentBranchAsync(Repository repository, CancellationToken cancellationToken)
    {
        var branch = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "symbolic-ref", "--quiet", "--short", "HEAD");
        if (string.IsNullOrWhiteSpace(branch)) throw new InvalidOperationException("HEAD is detached. This operation requires a current local branch.");
        return branch;
    }

    private static (int Ahead, int Behind) ParseTracking(string value)
    {
        static int ReadCount(string text, string marker)
        {
            var start = text.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return 0;
            start += marker.Length;
            var end = text.IndexOfAny([',', ']'], start);
            return int.TryParse(text[start..(end < 0 ? text.Length : end)].Trim(), out var count) ? count : 0;
        }
        return (ReadCount(value, "ahead "), ReadCount(value, "behind "));
    }

    private static void ValidateRefName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-') || value.Contains('\0') || value.Any(char.IsWhiteSpace))
            throw new ArgumentException("Invalid Git ref name.", parameterName);
    }

    public Task StageFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default)
    {
        ValidateChange(change);
        return RunGitForMutationAsync(repository, cancellationToken, PathArguments("add", change));
    }

    public Task StageAllAsync(Repository repository, CancellationToken cancellationToken = default) =>
        RunGitForMutationAsync(repository, cancellationToken, "add", "--all");

    public async Task UnstageFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default)
    {
        ValidateChange(change);
        if (await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "rev-parse", "--verify", "HEAD") is { Length: > 0 })
            await RunGitForMutationAsync(repository, cancellationToken, PathArguments("restore", change, "--staged"));
        else
            await RunGitForMutationAsync(repository, cancellationToken, PathArguments("rm", change, "--cached", "--ignore-unmatch"));
    }

    public async Task DiscardFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(change);
        ValidateChange(change);
        if (!change.IsUnstaged)
            throw new InvalidOperationException("Only unstaged working-tree changes can be discarded.");

        cancellationToken.ThrowIfCancellationRequested();
        if (change.IndexStatus == '?')
        {
            var fullPath = ResolveSafeWorkingTreePath(repository, change.Path);
            if (File.Exists(fullPath)) File.Delete(fullPath);
            return;
        }

        if (change.WorkingTreeStatus == 'R' && change.OriginalPath is not null)
        {
            var renamedPath = ResolveSafeWorkingTreePath(repository, change.Path);
            if (File.Exists(renamedPath)) File.Delete(renamedPath);
            await RunGitForMutationAsync(repository, cancellationToken, "restore", "--worktree", "--", change.OriginalPath);
            return;
        }

        await RunGitForMutationAsync(repository, cancellationToken, "restore", "--worktree", "--", change.Path);
    }

    public async Task CommitAsync(Repository repository, string message, bool amend = false, bool intentionalEmpty = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Enter a non-empty commit message.", nameof(message));
        var staged = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "diff", "--cached", "--name-only", "-z");
        if (staged.Length == 0 && !intentionalEmpty && !amend)
            throw new InvalidOperationException("The index is empty. Stage files or choose an intentional empty commit.");
        var arguments = new List<string> { "commit", "-m", message };
        if (amend) arguments.Add("--amend");
        if (intentionalEmpty) arguments.Add("--allow-empty");
        await RunGitForMutationAsync(repository, cancellationToken, arguments.ToArray());
    }

    private async Task<string> RunOptionalGitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        var result = await RunGitForResultAsync(
            workingDirectory,
            "RepositoryOptional",
            GitCommandKind.Internal,
            cancellationToken,
            null,
            arguments);
        if (result.ExitCode == 0) return result.StandardOutput;
        if (IsExpectedOptionalExitCode(arguments, result.ExitCode)) return string.Empty;
        throw CreateRepositoryCommandFailure(result);
    }

    private static bool IsExpectedOptionalExitCode(IReadOnlyList<string> arguments, int exitCode)
    {
        if (arguments.Count == 0) return false;
        return arguments[0] switch
        {
            "symbolic-ref" => exitCode == 1,
            "rev-parse" => exitCode is 1 or 128,
            "config" => exitCode == 1,
            "show" => exitCode == 128,
            _ => false
        };
    }

    private async Task EnsureGitAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await RunGitAsync(Environment.CurrentDirectory, cancellationToken, true, "--version");
        }
        catch (RepositoryOpenException exception)
        {
            throw new RepositoryOpenException("Git is installed but could not start correctly. Check the Git executable configuration.", exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RepositoryOpenException($"Git executable '{_executor.ExecutablePath}' was not found or could not be started.", exception);
        }
    }

    private Task<string> RunGitAsync(string workingDirectory, CancellationToken cancellationToken, bool requireOutput, params string[] arguments) =>
        RunGitAsync(workingDirectory, cancellationToken, requireOutput, null, GitCommandKind.Internal, arguments);

    private Task<string> RunGitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        bool requireOutput,
        IReadOnlyDictionary<string, string?>? environment,
        params string[] arguments) =>
        RunGitAsync(workingDirectory, cancellationToken, requireOutput, environment, GitCommandKind.Internal, arguments);

    private async Task<string> RunGitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        bool requireOutput,
        IReadOnlyDictionary<string, string?>? environment,
        GitCommandKind commandKind,
        params string[] arguments)
    {
        var result = await RunGitForResultAsync(
            workingDirectory,
            "Repository",
            commandKind,
            cancellationToken,
            environment,
            arguments);

        var output = result.StandardOutput;
        if (result.ExitCode != 0)
            throw CreateRepositoryCommandFailure(result);
        if (requireOutput && string.IsNullOrWhiteSpace(output))
            throw new RepositoryOpenException("Git command returned no output.");
        return output;
    }

    private async Task<GitCommandResult> RunGitForResultAsync(
        string workingDirectory,
        string operation,
        GitCommandKind commandKind,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment,
        IReadOnlyList<string> arguments)
    {
        try
        {
            return await _executor.ExecuteForResultAsync(
                workingDirectory,
                operation,
                commandKind,
                cancellationToken,
                environment,
                arguments);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RepositoryOpenException($"Git executable '{_executor.ExecutablePath}' could not be started.", exception);
        }
    }

    private static RepositoryOpenException CreateRepositoryCommandFailure(GitCommandResult result)
    {
        var error = result.StandardError.Trim();
        var detail = string.IsNullOrWhiteSpace(error) ? "Git returned no diagnostic message." : error;
        return new RepositoryOpenException($"Git command exited with code {result.ExitCode}: {detail}");
    }

    private async Task RunGitForMutationAsync(Repository repository, CancellationToken cancellationToken, params string[] arguments) =>
        _ = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, null, GitCommandKind.User, arguments);

    private async Task RunMergeToolAsync(Repository repository, CancellationToken cancellationToken, params string[] arguments)
    {
        try
        {
            await RunGitForMutationAsync(repository, cancellationToken, arguments);
        }
        catch (RepositoryOpenException exception)
        {
            throw new InvalidOperationException($"The external merge tool failed: {exception.Message}", exception);
        }
    }

    private async Task UnsetConfigurationAsync(Repository repository, string scope, string key, CancellationToken cancellationToken)
    {
        var result = await RunGitForResultAsync(
            repository.WorkingDirectory,
            "Repository",
            GitCommandKind.User,
            cancellationToken,
            null,
            ["config", scope, "--unset-all", key]);
        if (result.ExitCode is 0 or 5) return;
        throw CreateRepositoryCommandFailure(result);
    }

    private async Task<RepositoryOperationState> ReadOperationStateAsync(Repository repository, RepositoryOperation operation, string status, CancellationToken cancellationToken)
    {
        if (operation == RepositoryOperation.None) return RepositoryOperationState.None;

        var unmerged = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "ls-files", "--unmerged", "-z");
        var stages = ParseUnmergedStages(unmerged);
        var knownPaths = new HashSet<string>(stages.Keys, StringComparer.Ordinal);
        AddConflictPathsFromMessage(Path.Combine(repository.GitDirectory, "MERGE_MSG"), knownPaths);
        AddConflictPathsFromMessage(Path.Combine(repository.GitDirectory, "rebase-merge", "message"), knownPaths);
        AddConflictPathsFromMessage(Path.Combine(repository.GitDirectory, "rebase-apply", "final-commit"), knownPaths);

        var changes = ParseStatus(status).ToDictionary(change => change.Path, StringComparer.Ordinal);
        var conflicts = new List<ConflictFile>();
        foreach (var path in knownPaths.Order(StringComparer.Ordinal))
        {
            stages.TryGetValue(path, out var presentStages);
            presentStages ??= [];
            changes.TryGetValue(path, out var change);
            var unresolved = presentStages.Count > 0;
            var currentExists = presentStages.Contains(operation == RepositoryOperation.Rebase ? 3 : 2);
            var incomingExists = presentStages.Contains(operation == RepositoryOperation.Rebase ? 2 : 3);
            var isBinary = unresolved && await IsBinaryConflictAsync(repository, path, cancellationToken);
            var kind = isBinary ? ConflictKind.Binary : change is { IndexStatus: 'A', WorkingTreeStatus: 'A' } ? ConflictKind.AddAdd
                : currentExists && !incomingExists ? ConflictKind.ModifyDelete
                : !currentExists && incomingExists ? ConflictKind.DeleteModify
                : ConflictKind.Textual;
            var labels = operation == RepositoryOperation.Rebase
                ? ("Current/local (replayed commit)", "Incoming/remote (rebase base)")
                : ("Current/local", "Incoming/remote");
            conflicts.Add(new ConflictFile(path, kind, !unresolved,
                unresolved && !isBinary && File.Exists(Path.Combine(repository.WorkingDirectory, path)),
                unresolved && currentExists, unresolved && incomingExists,
                unresolved && (!currentExists || !incomingExists),
                unresolved && File.Exists(Path.Combine(repository.WorkingDirectory, path)),
                unresolved, labels.Item1, labels.Item2));
        }

        return new RepositoryOperationState(operation, conflicts,
            operation is RepositoryOperation.Merge or RepositoryOperation.Rebase or RepositoryOperation.CherryPick or RepositoryOperation.Revert,
            operation is RepositoryOperation.Merge or RepositoryOperation.Rebase or RepositoryOperation.CherryPick or RepositoryOperation.Revert,
            operation is RepositoryOperation.Rebase or RepositoryOperation.CherryPick or RepositoryOperation.Revert);
    }

    private async Task<bool> IsBinaryConflictAsync(Repository repository, string path, CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "diff", "--cached", "--numstat", "--", path);
        if (output.Split('\n').Any(line => line.StartsWith("-\t-\t", StringComparison.Ordinal))) return true;

        foreach (var stage in new[] { 2, 3 })
        {
            var blob = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "show", $":{stage}:{path}");
            if (blob.IndexOf('\0') >= 0) return true;
        }
        return false;
    }

    private static Dictionary<string, HashSet<int>> ParseUnmergedStages(string output)
    {
        var result = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0) continue;
            var metadata = entry[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length != 3 || !int.TryParse(metadata[2], out var stage)) continue;
            var path = entry[(tab + 1)..];
            if (!result.TryGetValue(path, out var values)) result[path] = values = [];
            values.Add(stage);
        }
        return result;
    }

    private static void AddConflictPathsFromMessage(string messagePath, HashSet<string> paths)
    {
        if (!File.Exists(messagePath)) return;
        foreach (var line in File.ReadLines(messagePath))
        {
            if (!line.StartsWith("#\t", StringComparison.Ordinal)) continue;
            var path = line[2..].TrimEnd();
            if (!string.IsNullOrWhiteSpace(path)) paths.Add(path);
        }
    }

    private async Task RunOperationCommandAsync(Repository repository, string action, CancellationToken cancellationToken)
    {
        var state = await ReadAsync(repository, cancellationToken);
        var allowed = action switch { "continue" => state.CurrentOperation.CanContinue, "abort" => state.CurrentOperation.CanAbort, "skip" => state.CurrentOperation.CanSkip, _ => false };
        if (!allowed) throw new InvalidOperationException($"The current Git operation does not support {action}.");
        var command = state.Operation switch
        {
            RepositoryOperation.Merge => "merge",
            RepositoryOperation.Rebase => "rebase",
            RepositoryOperation.CherryPick => "cherry-pick",
            RepositoryOperation.Revert => "revert",
            _ => throw new InvalidOperationException("No supported Git operation is in progress.")
        };
        if (state.Operation == RepositoryOperation.Rebase && action == "continue")
        {
            var result = await ContinueRebaseCoreAsync(repository, cancellationToken);
            if (result.Kind == RebaseResultKind.Failed) throw new InvalidOperationException(result.Message);
            return;
        }

        if (action == "continue" && state.Operation is RepositoryOperation.Merge or RepositoryOperation.CherryPick or RepositoryOperation.Revert)
        {
            var supportDirectory = Path.Combine(repository.GitDirectory, "csharpgit-merge");
            Directory.CreateDirectory(supportDirectory);
            var messageEditor = Path.Combine(supportDirectory, OperatingSystem.IsWindows() ? "message-editor.cmd" : "message-editor.sh");
            await WriteNoOpEditorAsync(messageEditor, cancellationToken);
            await RunGitAsync(repository.WorkingDirectory, cancellationToken, false,
                new Dictionary<string, string?> { ["GIT_EDITOR"] = QuoteCommand(messageEditor) },
                GitCommandKind.User,
                command, $"--{action}");
            return;
        }

        await RunGitForMutationAsync(repository, cancellationToken, command, $"--{action}");
    }

    private static void ValidateConflictAction(ConflictFile conflict, bool allowed)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ValidatePath(conflict.Path);
        if (!allowed) throw new InvalidOperationException("This action is unavailable for the selected conflict type or state.");
    }

    private static string BuildMergeToolArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return "\"$BASE\" \"$LOCAL\" \"$REMOTE\" \"$MERGED\"";
        EnsureMergeToolVariables(arguments);
        return arguments.Trim();
    }

    private static void EnsureMergeToolVariables(string command)
    {
        foreach (var variable in new[] { "$BASE", "$LOCAL", "$REMOTE", "$MERGED" })
            if (!command.Contains(variable, StringComparison.Ordinal))
                throw new ArgumentException($"The merge tool command must pass {variable}.");
    }

    private static IReadOnlyList<WorkingTreeChange> ParseStatus(string output)
    {
        var entries = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<WorkingTreeChange>();
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (entry.Length < 4) continue;
            string? originalPath = null;
            if ((entry[0] is 'R' or 'C' || entry[1] is 'R' or 'C') && index + 1 < entries.Length) originalPath = entries[++index];
            changes.Add(new WorkingTreeChange(entry[3..], entry[0], entry[1], originalPath));
        }
        return changes;
    }

    private static IReadOnlyDictionary<string, string> ParseConfiguration(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('\n');
            if (separator < 0) separator = entry.IndexOf('=');
            if (separator > 0) result[entry[..separator]] = entry[(separator + 1)..];
            else result[entry] = string.Empty;
        }
        return result;
    }

    private static RepositoryOperation DetectOperation(Repository repository)
    {
        if (Directory.Exists(Path.Combine(repository.GitDirectory, "rebase-merge")) || Directory.Exists(Path.Combine(repository.GitDirectory, "rebase-apply"))) return RepositoryOperation.Rebase;
        if (File.Exists(Path.Combine(repository.GitDirectory, "MERGE_HEAD"))) return RepositoryOperation.Merge;
        if (File.Exists(Path.Combine(repository.GitDirectory, "CHERRY_PICK_HEAD"))) return RepositoryOperation.CherryPick;
        if (File.Exists(Path.Combine(repository.GitDirectory, "REVERT_HEAD"))) return RepositoryOperation.Revert;
        if (File.Exists(Path.Combine(repository.GitDirectory, "BISECT_LOG"))) return RepositoryOperation.Bisect;
        return RepositoryOperation.None;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void ValidateObjectName(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Invalid commit hash.", nameof(hash));
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) throw new ArgumentException("Invalid file path.", nameof(path));
    }

    private static void ValidateChange(WorkingTreeChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ValidatePath(change.Path);
        if (change.OriginalPath is not null) ValidatePath(change.OriginalPath);
    }

    private static string[] PathArguments(string command, WorkingTreeChange change, params string[] options) =>
        [command, .. options, "--", change.Path, .. (change.OriginalPath is null ? Array.Empty<string>() : [change.OriginalPath])];

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
