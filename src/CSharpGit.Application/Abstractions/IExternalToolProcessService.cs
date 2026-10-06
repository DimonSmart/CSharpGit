namespace CSharpGit.Application.Abstractions;

public interface IExternalToolProcessService
{
    string? ResolveExecutable(string commandOrPath);

    bool IsExecutablePathUsable(string path);

    Task RunShellCommandAsync(
        string rawCommand,
        string fileArgument,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
