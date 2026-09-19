namespace CSharpGit.Presentation.ViewModels;

internal sealed record RepositoryMaintenanceMutationResult(
    bool Started,
    Exception? MutationFailure,
    Exception? RefreshFailure)
{
    public bool MutationSucceeded => Started && MutationFailure is null;
}

public sealed partial class OpenRepositoryViewModel
{
    internal async Task<RepositoryMaintenanceMutationResult> RunRepositoryMaintenanceMutationAsync(
        Func<Task> mutation,
        bool includeHistory,
        Func<Task>? additionalRefresh = null)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        if (!await _mutationGate.WaitAsync(0))
            return new RepositoryMaintenanceMutationResult(false, null, null);

        _isMutating = true;
        EnterBusy();
        RaiseCommands();

        try
        {
            Exception? mutationFailure = null;
            try
            {
                await mutation();
            }
            catch (Exception exception)
            {
                mutationFailure = exception;
            }

            Exception? refreshFailure = null;
            if (mutationFailure is null)
            {
                try
                {
                    await RefreshStateLocalOnlyAsync(includeHistory);
                }
                catch (Exception exception)
                {
                    refreshFailure = exception;
                }

                if (additionalRefresh is not null)
                {
                    try
                    {
                        await additionalRefresh();
                    }
                    catch (Exception exception)
                    {
                        refreshFailure ??= exception;
                    }
                }
            }

            return new RepositoryMaintenanceMutationResult(
                true,
                mutationFailure,
                refreshFailure);
        }
        finally
        {
            _isMutating = false;
            ExitBusy();
            _mutationGate.Release();
            RaiseCommands();
        }
    }
}
