namespace CSharpGit.Presentation.Threading;

/// <summary>Completes a queued UI operation even if dispatch is rejected or the owner expires.</summary>
internal static class UiDispatchCompletion
{
    internal static Task EnqueueAsync(
        Func<Action, bool> tryEnqueue,
        Action callback,
        Func<bool>? isOwnerCurrent = null)
    {
        ArgumentNullException.ThrowIfNull(tryEnqueue);
        ArgumentNullException.ThrowIfNull(callback);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            if (!tryEnqueue(() =>
            {
                try
                {
                    if (isOwnerCurrent is not null && !isOwnerCurrent())
                    {
                        completion.TrySetCanceled();
                        return;
                    }

                    callback();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }))
                completion.TrySetException(new InvalidOperationException("The UI dispatcher rejected the check."));
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }

        return completion.Task;
    }
}
