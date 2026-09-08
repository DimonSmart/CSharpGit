using System.Windows.Input;

namespace CSharpGit.Presentation.ViewModels;

internal sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    private bool _isExecuting;
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && canExecute();

    public async void Execute(object? parameter) => await ExecuteAsync();

    internal async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;
        _isExecuting = true;
        RaiseCanExecuteChanged();
        try { await execute(); }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
