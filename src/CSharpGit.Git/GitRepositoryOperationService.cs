using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitRepositoryOperationService : IRepositoryOperationService
{
    private readonly GitRepositoryCommandRunner _runner;
    private readonly GitRepositoryStateService _stateService;
    private readonly IInteractiveRebaseService _interactiveRebaseService;

    internal GitRepositoryOperationService(
        GitRepositoryCommandRunner runner,
        GitRepositoryStateService stateService,
        IInteractiveRebaseService interactiveRebaseService)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
        _interactiveRebaseService = interactiveRebaseService
            ?? throw new ArgumentNullException(nameof(interactiveRebaseService));
    }

    public Task ContinueOperationAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        RunOperationCommandAsync(repository, "continue", cancellationToken);

    public Task AbortOperationAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        RunOperationCommandAsync(repository, "abort", cancellationToken);

    public Task SkipOperationAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        RunOperationCommandAsync(repository, "skip", cancellationToken);

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
            var result = await _interactiveRebaseService.ContinueRebaseAsync(
                repository,
                cancellationToken);
            if (result.Kind == RebaseResultKind.Failed)
                throw new InvalidOperationException(result.Message);
            return;
        }

        if (state.Operation == RepositoryOperation.Rebase
            && action == "abort")
        {
            await _interactiveRebaseService.AbortRebaseAsync(
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

    private static string QuoteCommand(string path) =>
        $"\"{path.Replace("\"", "\\\"")}\"";
}
