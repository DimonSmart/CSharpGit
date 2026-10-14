using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Threading;

namespace CSharpGit.Presentation.ViewModels;

internal enum RepositoryMaintenancePhase
{
    Idle, LoadingStatistics, Ready, Running, Completed, Failed
}

internal enum RepositoryMaintenanceResultKind
{
    NotStarted, GcFailed, Completed
}

internal sealed record RepositoryMaintenanceResult(
    RepositoryMaintenanceResultKind Kind,
    RepositoryMaintenanceNotStartedReason NotStartedReason,
    RepositoryStorageStatistics? Before,
    RepositoryStorageStatistics? After,
    Exception? GcFailure,
    Exception? AfterStatisticsFailure,
    Exception? RefreshFailure);

// Independent feature workflow. Only the repository context owns the mutation gate.
internal sealed class RepositoryMaintenanceViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IRepositoryMaintenanceService _service;
    private readonly IUiDispatcher _dispatcher;
    private IRepositoryMaintenanceContext? _context;
    private Func<Task>? _refreshWorktrees;
    private int _generation;
    private bool _disposed;
    private bool _isRunning;
    private Repository? _preparedRepository;

    public RepositoryMaintenanceViewModel(
        IRepositoryMaintenanceService service,
        IUiDispatcher dispatcher)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RepositoryMaintenancePhase Phase { get; private set; } = RepositoryMaintenancePhase.Idle;
    public RepositoryStorageStatistics? Before { get; private set; }
    public RepositoryMaintenanceResult? Result { get; private set; }
    public Exception? PreparationFailure { get; private set; }
    public bool IsRunning => _isRunning;
    public bool CanStart => !_disposed && !_isRunning && _context?.CanStartRepositoryMaintenance == true;

    public void Attach(IRepositoryMaintenanceContext context, Func<Task> refreshWorktrees)
    {
        if (_context is not null)
            throw new InvalidOperationException("Repository maintenance is already attached.");
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _refreshWorktrees = refreshWorktrees ?? throw new ArgumentNullException(nameof(refreshWorktrees));
        _context.MaintenanceAvailabilityChanged += ContextAvailabilityChanged;
        Notify(nameof(CanStart));
    }

    private void ContextAvailabilityChanged(object? sender, EventArgs e) =>
        Publish(() => Notify(nameof(CanStart)));

    public async Task<bool> PrepareAsync(Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (_disposed || _isRunning || _context is null || !CanStart
            || !ReferenceEquals(repository, _context.CurrentMaintenanceRepository))
            return false;

        var generation = Interlocked.Increment(ref _generation);
        _preparedRepository = null;
        Before = null;
        Result = null;
        PreparationFailure = null;
        Phase = RepositoryMaintenancePhase.LoadingStatistics;
        NotifyState();

        try
        {
            var statistics = await _context.ReadMaintenanceStatisticsAsync(_service, repository);
            if (!IsCurrent(generation, repository) || statistics is null)
                return false;

            Before = statistics;
            _preparedRepository = repository;
            Phase = RepositoryMaintenancePhase.Ready;
            NotifyState();
            return true;
        }
        catch (OperationCanceledException) when (!IsCurrent(generation, repository))
        {
            return false;
        }
        catch (Exception exception)
        {
            if (!IsCurrent(generation, repository))
                return false;
            PreparationFailure = exception;
            Phase = RepositoryMaintenancePhase.Failed;
            NotifyState();
            return false;
        }
    }

    public async Task<RepositoryMaintenanceResult> ExecuteAsync(
        Repository repository,
        RepositoryGcOptions options)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);
        if (_context is null || _refreshWorktrees is null || _disposed || _isRunning
            || !ReferenceEquals(repository, _preparedRepository)
            || Before is null || !CanStart
            || !_context.IsCurrentMaintenanceRepository(repository))
            return NotStarted(RepositoryMaintenanceNotStartedReason.StaleContext);

        _isRunning = true; // Installed before the first await and held through gate release.
        Phase = RepositoryMaintenancePhase.Running;
        NotifyState();

        var before = Before;
        RepositoryStorageStatistics? after = null;
        Exception? afterFailure = null;
        try
        {
            var mutation = await _context.RunRepositoryMaintenanceMutationAsync(
                repository,
                async () =>
                {
                    await _service.GarbageCollectAsync(repository, options);
                    try
                    {
                        after = await _service.GetStorageStatisticsAsync(repository);
                    }
                    catch (Exception exception)
                    {
                        afterFailure = exception;
                    }
                },
                includeHistory: _context.ShowMaintenanceReflog,
                additionalRefresh: _refreshWorktrees);

            var result = !mutation.Started
                ? NotStarted(mutation.NotStartedReason)
                : new RepositoryMaintenanceResult(
                    mutation.MutationSucceeded
                        ? RepositoryMaintenanceResultKind.Completed
                        : RepositoryMaintenanceResultKind.GcFailed,
                    RepositoryMaintenanceNotStartedReason.None,
                    before,
                    after,
                    mutation.MutationFailure,
                    afterFailure,
                    mutation.RefreshFailure);
            Result = result;
            Phase = result.Kind == RepositoryMaintenanceResultKind.Completed
                ? RepositoryMaintenancePhase.Completed
                : RepositoryMaintenancePhase.Failed;
            NotifyState();
            return result;
        }
        finally
        {
            _isRunning = false; // The existing gate, including targeted refresh, has been released.
            NotifyState();
        }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _preparedRepository = null;
        if (_isRunning) return;
        Before = null;
        Result = null;
        PreparationFailure = null;
        Phase = RepositoryMaintenancePhase.Idle;
        NotifyState();
    }

    private bool IsCurrent(int generation, Repository repository) =>
        !_disposed && generation == Volatile.Read(ref _generation)
        && _context?.IsCurrentMaintenanceRepository(repository) == true;

    private static RepositoryMaintenanceResult NotStarted(RepositoryMaintenanceNotStartedReason reason) =>
        new(RepositoryMaintenanceResultKind.NotStarted, reason, null, null, null, null, null);

    private void NotifyState()
    {
        Notify(nameof(Phase));
        Notify(nameof(Before));
        Notify(nameof(Result));
        Notify(nameof(PreparationFailure));
        Notify(nameof(IsRunning));
        Notify(nameof(CanStart));
    }

    private void Notify([CallerMemberName] string? property = null) =>
        Publish(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property)));

    private void Publish(Action action)
    {
        if (_disposed) return;
        if (_dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(() => { if (!_disposed) action(); });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _generation);
        if (_context is not null)
            _context.MaintenanceAvailabilityChanged -= ContextAvailabilityChanged;
    }
}
