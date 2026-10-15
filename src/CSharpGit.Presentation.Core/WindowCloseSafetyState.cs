namespace CSharpGit.Presentation;

// Pure representation of the existing close-admission contract. The MainPage
// supplies production operation flags; it owns no new lifecycle state.
internal readonly record struct WindowCloseSafetyState(
    bool IsMutating,
    bool IsChangingRepository,
    bool IsHistoryRewriteRunning,
    bool IsMaintenanceRunning,
    bool IsUpdateRunning,
    bool IsCloneRunning,
    bool IsCreateRunning)
{
    public bool IsUnsafeToClose =>
        IsMutating || IsChangingRepository || IsHistoryRewriteRunning
        || IsMaintenanceRunning || IsUpdateRunning || IsCloneRunning || IsCreateRunning;
}
