using System.ComponentModel;
using System.Windows.Input;
using CSharpGit.Application;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel
{
    private WorkingTreeDiscardRequest? _pendingBatchDiscard;
    private bool _discardCommandNotificationsAttached;
    private AsyncCommand? _requestDiscardSelectedCommand;
    private AsyncCommand? _requestDiscardAllCommand;
    private AsyncCommand? _confirmBatchDiscardCommand;
    private AsyncCommand? _cancelBatchDiscardCommand;

    public ICommand RequestDiscardSelectedCommand =>
        _requestDiscardSelectedCommand ??= CreateDiscardCommand(
            RequestDiscardSelectedAsync,
            () => CanMutate() && WorkingTreeDiscard.CanDiscardSelected(_selectedUnstagedChanges));

    public ICommand RequestDiscardAllCommand =>
        _requestDiscardAllCommand ??= CreateDiscardCommand(
            RequestDiscardAllAsync,
            () => CanMutate() && Changes.Any(WorkingTreeDiscard.IsEligible));

    public ICommand ConfirmBatchDiscardCommand =>
        _confirmBatchDiscardCommand ??= CreateDiscardCommand(
            ConfirmBatchDiscardAsync,
            () => CanMutate() && _pendingBatchDiscard is not null);

    public ICommand CancelBatchDiscardCommand =>
        _cancelBatchDiscardCommand ??= CreateDiscardCommand(
            CancelBatchDiscardAsync,
            () => _pendingBatchDiscard is not null);

    public Visibility BatchDiscardConfirmationVisibility =>
        _pendingBatchDiscard is null ? Visibility.Collapsed : Visibility.Visible;

    public string BatchDiscardConfirmationMessage =>
        _pendingBatchDiscard?.ConfirmationMessage ?? string.Empty;

    private AsyncCommand CreateDiscardCommand(Func<Task> execute, Func<bool> canExecute)
    {
        EnsureDiscardCommandNotifications();
        return new AsyncCommand(execute, canExecute);
    }

    private void EnsureDiscardCommandNotifications()
    {
        if (_discardCommandNotificationsAttached) return;
        _discardCommandNotificationsAttached = true;
        PropertyChanged += DiscardCommandPropertyChanged;
        Changes.CollectionChanged += (_, _) => RaiseBatchDiscardCommands();
    }

    private void DiscardCommandPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SelectedUnstagedChanges) or nameof(IsBusy) or nameof(Repository))
            RaiseBatchDiscardCommands();
    }

    private void RaiseBatchDiscardCommands()
    {
        _requestDiscardSelectedCommand?.RaiseCanExecuteChanged();
        _requestDiscardAllCommand?.RaiseCanExecuteChanged();
        _confirmBatchDiscardCommand?.RaiseCanExecuteChanged();
        _cancelBatchDiscardCommand?.RaiseCanExecuteChanged();
    }

    private Task RequestDiscardSelectedAsync()
    {
        SetPendingBatchDiscard(WorkingTreeDiscard.CreateSelected(_selectedUnstagedChanges));
        return Task.CompletedTask;
    }

    private Task RequestDiscardAllAsync()
    {
        SetPendingBatchDiscard(WorkingTreeDiscard.CreateAll(Changes));
        return Task.CompletedTask;
    }

    private Task CancelBatchDiscardAsync()
    {
        SetPendingBatchDiscard(null);
        return Task.CompletedTask;
    }

    private async Task ConfirmBatchDiscardAsync()
    {
        var request = _pendingBatchDiscard;
        var repository = Repository;
        SetPendingBatchDiscard(null);
        if (request is null || repository is null) return;

        IReadOnlyList<WorkingTreeDiscardResult>? results = null;
        await MutateAsync(
            async () =>
            {
                results = await WorkingTreeDiscard.ExecuteAsync(
                    repository,
                    request,
                    (change, cancellationToken) =>
                        _workingTreeService.DiscardFileAsync(repository, change, cancellationToken));
            },
            "Could not discard changes",
            ClearWorkingTreePresentationSelection,
            includeHistory: false);

        if (results is null) return;
        var failureMessage = WorkingTreeDiscard.FormatFailures(results);
        if (failureMessage.Length == 0) return;

        ErrorMessage = string.IsNullOrWhiteSpace(ErrorMessage)
            ? failureMessage
            : $"{failureMessage}{Environment.NewLine}{ErrorMessage}";
    }

    private void SetPendingBatchDiscard(WorkingTreeDiscardRequest? request)
    {
        _pendingBatchDiscard = request;
        Notify(nameof(BatchDiscardConfirmationVisibility));
        Notify(nameof(BatchDiscardConfirmationMessage));
        RaiseBatchDiscardCommands();
    }
}
