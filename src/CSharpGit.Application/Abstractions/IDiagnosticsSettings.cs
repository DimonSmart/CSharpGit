namespace CSharpGit.Application.Abstractions;

/// <summary>Logging, Git Console and history diagnostics preferences.</summary>
public interface IDiagnosticsSettings : IAppSettingsChangeSource
{
    bool LoggingEnabled { get; }
    ApplicationLogLevel LogLevel { get; }
    GitConsoleAutoOpenMode GitConsoleAutoOpenMode { get; }
    bool HistoryPerformanceDiagnosticsEnabled { get; }

    Task SetLoggingSettingsAsync(bool enabled, ApplicationLogLevel level, CancellationToken cancellationToken = default);
    Task SetGitConsoleAutoOpenModeAsync(GitConsoleAutoOpenMode mode, CancellationToken cancellationToken = default);
    Task SetHistoryPerformanceDiagnosticsEnabledAsync(bool value, CancellationToken cancellationToken = default);
}
