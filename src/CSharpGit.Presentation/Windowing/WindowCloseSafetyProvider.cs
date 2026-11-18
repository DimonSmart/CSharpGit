using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Windowing;

// Reads the authoritative operation models on every request. No mirrored busy flags.
internal sealed class WindowCloseSafetyProvider(
    OpenRepositoryViewModel repository,
    CloneRepositoryViewModel clone,
    CreateRepositoryViewModel create,
    RepositoryMaintenanceViewModel maintenance,
    ApplicationUpdateActivityState updateActivity)
{
    internal bool IsUnsafeToClose => new WindowCloseSafetyState(
        IsMutating: repository.IsMutating,
        IsChangingRepository: repository.IsChangingRepository,
        IsHistoryRewriteRunning: repository.RepositoryHistoryRewrite.IsInProgress,
        IsMaintenanceRunning: maintenance.IsRunning,
        IsUpdateRunning: updateActivity.IsRunning,
        IsCloneRunning: clone.IsBusy,
        IsCreateRunning: create.IsBusy).IsUnsafeToClose;

    internal bool RequiresCloseConfirmation => repository.CommitCreation.HasUnappliedCommitMessage;
}
