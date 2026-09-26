using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel
{
    internal async Task<bool> PrepareInteractiveRebaseFromCommitAsync(string fullSha)
    {
        var repository = Repository;
        var firstCommit = fullSha;
        if (repository is null || string.IsNullOrWhiteSpace(firstCommit)) return false;

        EnterBusy();
        ErrorMessage = null;
        InvalidatePreparedRebasePlan();
        try
        {
            var plan = await _workflowService.ReadInteractiveRebasePlanFromCommitAsync(
                repository,
                firstCommit);

            if (!ReferenceEquals(repository, Repository))
                return false;

            ApplyPreparedRebasePlan(plan);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = $"Git: {exception.Message}";
            return false;
        }
        finally
        {
            ExitBusy();
            RaiseCommands();
        }
    }

    private void ApplyPreparedRebasePlan(InteractiveRebasePlan plan)
    {
        _rebaseOnto = plan.Onto;
        Notify(nameof(RebaseOnto));
        _rebaseSourceSnapshot = plan.SourceSnapshot;
        Replace(RebasePlan, plan.Items);
        SelectedRebaseItem = RebasePlan.FirstOrDefault();
    }

    private void InvalidatePreparedRebasePlan()
    {
        _rebaseSourceSnapshot = null;
        RebasePlan.Clear();
        SelectedRebaseItem = null;
    }
}
