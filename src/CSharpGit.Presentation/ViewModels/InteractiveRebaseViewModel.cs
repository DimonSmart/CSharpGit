using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public interface IInteractiveRebaseRepositoryContext : INotifyPropertyChanged
{
    Repository? Repository { get; }
    bool IsBusy { get; }
    RepositoryOperation CurrentOperation { get; }

    Task<bool> RunInteractiveRebaseMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string? errorContext = null);

    void PublishOperationMessage(string message);
    void ReportInteractiveRebaseError(string message);
}

public sealed class InteractiveRebaseViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IInteractiveRebaseService _interactiveRebaseService;
    private readonly IRepositoryIdentityService _repositoryIdentityService;
    private readonly IInteractiveRebaseAuthorChangeService _authorChangeService;
    private readonly ICommitAuthorDateReader _commitAuthorDateReader;
    private IInteractiveRebaseRepositoryContext? _context;
    private CancellationTokenSource? _prepareCancellation;
    private InteractiveRebaseTodo? _preparedTodo;
    private string _rebaseOnto = string.Empty;
    private string _rebaseTodoText = string.Empty;
    private int _disposed;

    public InteractiveRebaseViewModel(
        IInteractiveRebaseService interactiveRebaseService,
        IRepositoryIdentityService repositoryIdentityService,
        IInteractiveRebaseAuthorChangeService authorChangeService,
        ICommitAuthorDateReader commitAuthorDateReader)
    {
        _interactiveRebaseService = interactiveRebaseService ?? throw new ArgumentNullException(nameof(interactiveRebaseService));
        _repositoryIdentityService = repositoryIdentityService ?? throw new ArgumentNullException(nameof(repositoryIdentityService));
        _authorChangeService = authorChangeService ?? throw new ArgumentNullException(nameof(authorChangeService));
        _commitAuthorDateReader = commitAuthorDateReader ?? throw new ArgumentNullException(nameof(commitAuthorDateReader));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RebaseOnto
    {
        get => _rebaseOnto;
        private set
        {
            if (string.Equals(_rebaseOnto, value, StringComparison.Ordinal)) return;
            _rebaseOnto = value;
            Notify();
        }
    }

    public string RebaseTodoText
    {
        get => _rebaseTodoText;
        set
        {
            if (string.Equals(_rebaseTodoText, value, StringComparison.Ordinal)) return;
            _rebaseTodoText = value;
            Notify();
        }
    }

    internal void Attach(IInteractiveRebaseRepositoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (ReferenceEquals(_context, context)) return;

        Detach();
        _context = context;
        _context.PropertyChanged += Context_PropertyChanged;
    }

    internal void Detach()
    {
        if (_context is not null)
            _context.PropertyChanged -= Context_PropertyChanged;
        _context = null;
        ClearRepositoryState();
    }

    internal async Task<bool> PrepareInteractiveRebaseFromCommitAsync(string fullSha)
    {
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null || string.IsNullOrWhiteSpace(fullSha))
            return false;

        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _prepareCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        ClearPreparedState(cancelPreparation: false);

        try
        {
            var todo = await _interactiveRebaseService.ReadInteractiveRebaseTodoFromCommitAsync(
                repository,
                fullSha,
                cancellation.Token);

            if (cancellation.IsCancellationRequested
                || !ReferenceEquals(repository, _context?.Repository))
            {
                return false;
            }

            _preparedTodo = todo;
            RebaseOnto = todo.Onto;
            RebaseTodoText = todo.TodoText;
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (ReferenceEquals(repository, _context?.Repository))
                context.ReportInteractiveRebaseError($"Git: {exception.Message}");
            return false;
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _prepareCancellation, null, cancellation),
                    cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    internal async Task<RepositoryIdentitySnapshot?> ReadInteractiveRebaseAuthorIdentityAsync()
    {
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null)
            return null;

        var snapshot = await _repositoryIdentityService.ReadAsync(repository);
        return ReferenceEquals(repository, _context?.Repository)
            ? snapshot
            : null;
    }

    internal InteractiveRebaseAuthorChangeAnalysis AnalyzeInteractiveRebaseAuthorChange(
        string todoText,
        int selectionStart,
        int selectionLength) =>
        _authorChangeService.Analyze(todoText, selectionStart, selectionLength);

    internal async Task<InteractiveRebaseAuthorChangeResult> ApplyInteractiveRebaseAuthorChangeAsync(
        InteractiveRebaseAuthorChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ResetToCurrentGitIdentity || request.ResetAuthorDate)
            return _authorChangeService.Apply(request);

        var repository = _context?.Repository
            ?? throw new InvalidOperationException("No repository is open.");
        var commits = _authorChangeService.GetTargetCommits(request);
        var authorDates = await _commitAuthorDateReader.ReadCommitAuthorDatesAsync(
            repository,
            commits);

        if (!ReferenceEquals(repository, _context?.Repository))
        {
            throw new InvalidOperationException(
                "The open repository changed while commit metadata was being read.");
        }

        return _authorChangeService.Apply(request, authorDates);
    }

    internal async Task StartPreparedInteractiveRebaseAsync(string todoText)
    {
        var context = _context;
        var repository = context?.Repository;
        var prepared = _preparedTodo;
        if (context is null || repository is null || prepared is null)
            return;

        RebaseTodoText = todoText;
        RebaseResult? result = null;
        try
        {
            await context.RunInteractiveRebaseMutationAsync(
                repository,
                async () =>
                    result = await _interactiveRebaseService.StartInteractiveRebaseTodoAsync(
                        repository,
                        prepared with { TodoText = RebaseTodoText }));

            if (result is not null)
                context.PublishOperationMessage(result.Message);
        }
        finally
        {
            ClearPreparedState(cancelPreparation: false);
        }
    }

    internal void Invalidate() => ClearPreparedState(cancelPreparation: true);

    internal void ClearRepositoryState() => Invalidate();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Detach();
    }

    private void ClearPreparedState(bool cancelPreparation)
    {
        _preparedTodo = null;
        RebaseOnto = string.Empty;
        RebaseTodoText = string.Empty;

        if (!cancelPreparation)
            return;

        var cancellation = Interlocked.Exchange(ref _prepareCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IInteractiveRebaseRepositoryContext.Repository))
            ClearRepositoryState();
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
