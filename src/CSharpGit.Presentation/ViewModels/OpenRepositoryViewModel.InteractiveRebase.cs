using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel
{
    internal async Task<bool> PrepareInteractiveRebaseAsync()
    {
        var repository = Repository;
        var onto = RebaseOnto;
        if (repository is null || string.IsNullOrWhiteSpace(onto))
            return false;

        EnterBusy();
        ErrorMessage = null;
        InvalidatePreparedInteractiveRebaseTodo();
        try
        {
            var todo = await _workflowService.ReadInteractiveRebaseTodoAsync(
                repository,
                onto);

            if (!ReferenceEquals(repository, Repository)
                || !string.Equals(RebaseOnto, onto, StringComparison.Ordinal))
                return false;

            ApplyPreparedInteractiveRebaseTodo(todo);
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

    internal async Task<bool> PrepareInteractiveRebaseFromCommitAsync(string fullSha)
    {
        var repository = Repository;
        if (repository is null || string.IsNullOrWhiteSpace(fullSha))
            return false;

        EnterBusy();
        ErrorMessage = null;
        InvalidatePreparedInteractiveRebaseTodo();
        try
        {
            var todo = await _workflowService.ReadInteractiveRebaseTodoFromCommitAsync(
                repository,
                fullSha);

            if (!ReferenceEquals(repository, Repository))
                return false;

            ApplyPreparedInteractiveRebaseTodo(todo);
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

    internal async Task StartPreparedInteractiveRebaseAsync(string todoText)
    {
        var repository = Repository;
        var prepared = _preparedInteractiveRebaseTodo;
        if (repository is null || prepared is null)
            return;

        RebaseTodoText = todoText;
        RebaseResult? result = null;
        try
        {
            await MutateAsync(
                async () =>
                    result = await _workflowService.StartInteractiveRebaseTodoAsync(
                        repository,
                        prepared with { TodoText = RebaseTodoText }));

            if (result is not null)
                OperationDisplay = result.Message;
        }
        finally
        {
            InvalidatePreparedInteractiveRebaseTodo();
        }
    }

    private void ApplyPreparedInteractiveRebaseTodo(InteractiveRebaseTodo todo)
    {
        _rebaseOnto = todo.Onto;
        Notify(nameof(RebaseOnto));
        _preparedInteractiveRebaseTodo = todo;
        RebaseTodoText = todo.TodoText;
    }

    private void InvalidatePreparedInteractiveRebaseTodo()
    {
        _preparedInteractiveRebaseTodo = null;
        if (_rebaseTodoText.Length == 0)
            return;

        _rebaseTodoText = string.Empty;
        Notify(nameof(RebaseTodoText));
    }
}
