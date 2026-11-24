using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Diagnostics;

// Narrow desktop lifecycle test seam; no visual root or production workflow ownership.
internal sealed record DesktopLifecycleCheckContext(
    Func<bool> RefreshRequired,
    Func<Action> CapturePendingMonitorDeliveryForDesktopCheck,
    Func<string, Task<bool>> ConfirmedSwitchForDesktopCheckAsync,
    Func<bool> ConfirmedCloseForDesktopCheck,
    CloneRepositoryViewModel DesktopCloneForCheck,
    CreateRepositoryViewModel DesktopCreateForCheck,
    RepositoryMaintenanceViewModel DesktopMaintenanceForCheck)
{
    internal bool IsRefreshRequired => RefreshRequired();
}
