using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Extensions.Logging;

namespace CSharpGit.Presentation.ViewModels;

internal sealed class AsyncCommand(
    Func<Task> execute,
    Func<bool> canExecute,
    ILogger? logger = null,
    Action<Exception>? errorHandler = null) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && canExecute();

    // ICommand is void-returning. Contain failures here; direct callers of
    // ExecuteAsync still receive the original exception.
    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync();
        }
        catch (OperationCanceledException)
        {
            // A cancelled UI command is not a user-facing error.
        }
        catch (Exception exception)
        {
            if (logger is null)
                Trace.TraceError($"Async command failed: {exception}");
            else
                logger.LogError(exception, "Async UI command failed");

            try
            {
                errorHandler?.Invoke(exception);
            }
            catch (Exception handlerException)
            {
                Trace.TraceError($"Async command error handler failed: {handlerException}");
            }
        }
    }

    internal async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;
        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
