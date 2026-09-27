using CSharpGit.Application;

namespace CSharpGit.Application.Abstractions;

public interface IApplicationVersionProvider
{
    string DisplayVersion { get; }

    ReleaseVersion? ReleaseVersion { get; }
}

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public interface IApplicationUpdateInstaller
{
    Task<ApplicationUpdateAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task<ApplicationUpdateResult> InstallAsync(
        ReleaseVersion expectedVersion,
        IProgress<ApplicationUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IProcessExecutor
{
    Task<ProcessExecutionResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

public interface IApplicationUpdateEnvironment
{
    bool IsMacOS { get; }

    string? ProcessPath { get; }

    string? PathValue { get; }

    char PathSeparator { get; }

    bool FileExists(string path);

    bool IsExecutableFile(string path);
}

public interface ISystemUriLauncher
{
    Task OpenUriAsync(Uri uri, CancellationToken cancellationToken = default);
}
