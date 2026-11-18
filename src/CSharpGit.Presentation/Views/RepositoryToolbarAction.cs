namespace CSharpGit.Presentation.Views;

/// <summary>Only actions exposed by the repository toolbar. Git/dialog workflows remain in phase 3.</summary>
internal enum RepositoryToolbarAction
{
    CommitNavigation,
    Fetch,
    Pull,
    PullFastForwardOnly,
    PullMerge,
    PullRebase,
    PullSettings,
    PushPrimary,
    Push,
    PushTo,
    ForcePushWithLease,
    PushSetDefault,
    PushToSetDefault,
    ForcePushWithLeaseSetDefault,
    RefreshIndicator,
    OpenRepository,
    CloneRepository,
    CreateRepository,
    Repositories,
    CloseRepository,
    RepositorySettings,
    OptimizeRepository,
    Settings,
    About,
}
