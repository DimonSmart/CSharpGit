using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public interface ICommitCreationRepositoryContext
{
    Repository? Repository { get; }
    bool CanRunRepositoryMutation { get; }
    bool CanRunBulkMutation { get; }
    bool HasStagedChanges { get; }
    bool HasUnstagedChanges { get; }

    Task<bool> RunCommitMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string? errorContext = null);

    void ClearWorkingTreePresentationSelection();

    void ClearCommittedWorkingTreePresentationSelection();
}

public sealed class CommitCreationViewModel : INotifyPropertyChanged
{
    private readonly IWorkingTreeService _workingTreeService;
    private ICommitCreationRepositoryContext? _context;
    private Repository? _pendingRepository;
    private string _commitMessage = string.Empty;
    private bool _isEmptyIndexChoiceOpen;

    public CommitCreationViewModel(IWorkingTreeService workingTreeService)
    {
        _workingTreeService = workingTreeService ?? throw new ArgumentNullException(nameof(workingTreeService));

        CommitCommand = new AsyncCommand(RequestCommitAsync, CanRunMessageMutation);
        EmptyCommitCommand = new AsyncCommand(
            () => CommitCurrentRepositoryAsync(amend: false, intentionalEmpty: true),
            CanRunMessageMutation);
        AmendCommand = new AsyncCommand(
            () => CommitCurrentRepositoryAsync(amend: true, intentionalEmpty: false),
            CanRunMessageMutation);
        StageAllAndCommitCommand = new AsyncCommand(
            StageAllAndCommitAsync,
            () => _context?.CanRunBulkMutation == true && IsEmptyIndexChoiceOpen);
        ConfirmEmptyCommitCommand = new AsyncCommand(
            ConfirmEmptyCommitAsync,
            () => _context?.CanRunRepositoryMutation == true && IsEmptyIndexChoiceOpen);
        CancelCommitCommand = new AsyncCommand(
            CancelCommitAsync,
            () => IsEmptyIndexChoiceOpen);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CommitCommand { get; }

    public ICommand EmptyCommitCommand { get; }

    public ICommand AmendCommand { get; }

    public ICommand StageAllAndCommitCommand { get; }

    public ICommand ConfirmEmptyCommitCommand { get; }

    public ICommand CancelCommitCommand { get; }

    public string CommitMessage
    {
        get => _commitMessage;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_commitMessage, value, StringComparison.Ordinal)) return;

            _commitMessage = value;
            Notify();
            Notify(nameof(HasUnappliedCommitMessage));
            RefreshAvailability();
        }
    }

    public bool HasUnappliedCommitMessage => CommitMessage.Length > 0;

    public bool IsEmptyIndexChoiceOpen
    {
        get => _isEmptyIndexChoiceOpen;
        private set
        {
            if (_isEmptyIndexChoiceOpen == value) return;

            _isEmptyIndexChoiceOpen = value;
            Notify();
            RefreshAvailability();
        }
    }

    internal void Attach(ICommitCreationRepositoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        RefreshAvailability();
    }

    internal void RefreshAvailability()
    {
        foreach (var command in new[]
                 {
                     CommitCommand,
                     EmptyCommitCommand,
                     AmendCommand,
                     StageAllAndCommitCommand,
                     ConfirmEmptyCommitCommand,
                     CancelCommitCommand
                 }.OfType<AsyncCommand>())
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private bool CanRunMessageMutation() =>
        _context?.Repository is not null
        && _context.CanRunRepositoryMutation
        && !string.IsNullOrWhiteSpace(CommitMessage);

    private Task RequestCommitAsync()
    {
        var context = _context;
        var repository = context?.Repository;
        if (context is null
            || repository is null
            || !context.CanRunRepositoryMutation)
        {
            return Task.CompletedTask;
        }

        if (!context.HasStagedChanges && context.HasUnstagedChanges)
        {
            _pendingRepository = repository;
            IsEmptyIndexChoiceOpen = true;
            return Task.CompletedTask;
        }

        return CommitAsync(repository, amend: false, intentionalEmpty: false);
    }

    private Task CommitCurrentRepositoryAsync(bool amend, bool intentionalEmpty)
    {
        var context = _context;
        var repository = context?.Repository;
        return context is null
               || repository is null
               || !context.CanRunRepositoryMutation
            ? Task.CompletedTask
            : CommitAsync(repository, amend, intentionalEmpty);
    }

    private async Task StageAllAndCommitAsync()
    {
        var context = _context;
        var repository = _pendingRepository;
        CloseEmptyIndexChoice();

        if (context is null
            || repository is null
            || !context.CanRunBulkMutation)
        {
            return;
        }

        var message = CommitMessage;
        var succeeded = await context.RunCommitMutationAsync(
            repository,
            async () =>
            {
                context.ClearWorkingTreePresentationSelection();
                await _workingTreeService.StageAllAsync(repository);
                await _workingTreeService.CommitAsync(repository, message);
            },
            "Could not stage all files and commit");

        if (succeeded && string.Equals(CommitMessage, message, StringComparison.Ordinal))
            CommitMessage = string.Empty;
    }

    private Task ConfirmEmptyCommitAsync()
    {
        var context = _context;
        var repository = _pendingRepository;
        CloseEmptyIndexChoice();

        return context is null
               || repository is null
               || !context.CanRunRepositoryMutation
            ? Task.CompletedTask
            : CommitAsync(repository, amend: false, intentionalEmpty: true);
    }

    private Task CancelCommitAsync()
    {
        CloseEmptyIndexChoice();
        return Task.CompletedTask;
    }

    private async Task CommitAsync(
        Repository repository,
        bool amend,
        bool intentionalEmpty)
    {
        var context = _context;
        if (context is null)
            return;

        var message = CommitMessage;
        var succeeded = await context.RunCommitMutationAsync(
            repository,
            async () =>
            {
                await _workingTreeService.CommitAsync(
                    repository,
                    message,
                    amend,
                    intentionalEmpty);
                context.ClearCommittedWorkingTreePresentationSelection();
            });

        if (succeeded && string.Equals(CommitMessage, message, StringComparison.Ordinal))
            CommitMessage = string.Empty;
    }

    private void CloseEmptyIndexChoice()
    {
        _pendingRepository = null;
        IsEmptyIndexChoiceOpen = false;
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
