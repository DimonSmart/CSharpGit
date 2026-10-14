namespace CSharpGit.Application.Abstractions;

/// <summary>Preferences governing Git synchronization workflows.</summary>
public interface IGitWorkflowSettings : IAppSettingsChangeSource
{
    bool AutoSetupRemoteOnPush { get; }
    PullStrategy DefaultPullStrategy { get; }
    bool ForcePullAutoStash { get; }

    Task SetAutoSetupRemoteOnPushAsync(bool value, CancellationToken cancellationToken = default);
    Task SetDefaultPullStrategyAsync(PullStrategy strategy, CancellationToken cancellationToken = default);
    Task SetForcePullAutoStashAsync(bool value, CancellationToken cancellationToken = default);
}
