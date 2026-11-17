using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Threading;

namespace CSharpGit.Presentation;

/// <summary>
/// Owns repository watcher sessions, background working-tree checks and refresh
/// invalidation state. All state transitions run on the UI dispatcher; only the
/// watcher callback crosses threads.
/// </summary>
internal sealed class RepositoryChangeCoordinator : IDisposable
{
    private readonly IWorkingTreeStatusReader _statusReader;
    private readonly IUiDispatcher _dispatcher;
    private readonly RepositoryChangeMonitor _monitor;
    private readonly RepositoryRefreshLifecycleState _lifecycle = new();
    private readonly CancellationTokenSource _lifetimeStop = new();

    private CancellationTokenSource? _repositoryStatusStop;
    private Repository? _monitoredRepository;
    private WorkingTreeStatusSnapshot? _displayedStatus;
    private RepositoryInvalidationBatch? _pendingStatus;
    private Repository? _refreshWindowRepository;
    private long _refreshStartGeneration;
    private long _handledGeneration;
    private long _deliveryEpoch;
    private bool _refreshWindowActive;
    private bool _statusRunning;
    private bool _initialized;
    private bool _disposed;

    internal RepositoryChangeCoordinator(
        IWorkingTreeStatusReader statusReader,
        IUiDispatcher dispatcher,
        RepositoryChangeMonitor? monitor = null)
    {
        _statusReader = statusReader ?? throw new ArgumentNullException(nameof(statusReader));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _monitor = monitor ?? new RepositoryChangeMonitor();
    }

    public event Action? RefreshRequirementChanged;

    public bool IsRefreshRequired => _lifecycle.IsRefreshRequired;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return;

        _repositoryStatusStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeStop.Token);
        _monitor.RepositoryChanged += Monitor_RepositoryChanged;
        _initialized = true;
    }

    public void ChangeRepository(
        Repository? repository,
        WorkingTreeStatusSnapshot? displayedStatus,
        long baselineRevision)
    {
        if (!_initialized || _disposed) return;

        if (!ReferenceEquals(repository, _monitoredRepository))
        {
            _deliveryEpoch++;
            _repositoryStatusStop?.Cancel();
            _repositoryStatusStop?.Dispose();
            _repositoryStatusStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeStop.Token);
        }

        if (repository is null)
        {
            _monitoredRepository = null;
            _displayedStatus = null;
            _pendingStatus = null;
            _refreshWindowActive = false;
            _refreshWindowRepository = null;
            _handledGeneration = _monitor.Generation;
            _lifecycle.Reset(baselineRevision);
            _monitor.Stop();
            RefreshRequirementChanged?.Invoke();
            return;
        }

        if (ReferenceEquals(repository, _monitoredRepository)) return;

        _monitoredRepository = repository;
        _displayedStatus = displayedStatus;
        _pendingStatus = null;
        _refreshWindowActive = false;
        _refreshWindowRepository = null;
        _lifecycle.Reset(baselineRevision);
        _monitor.Start(repository);
        _handledGeneration = _monitor.Generation;
        RefreshRequirementChanged?.Invoke();
    }

    public void PublishBaseline(
        Repository? repository,
        WorkingTreeStatusSnapshot? displayedStatus,
        long baselineRevision)
    {
        if (!_initialized || _disposed || displayedStatus is null
            || !ReferenceEquals(repository, _monitoredRepository))
            return;

        var startGeneration = _refreshWindowActive
            && ReferenceEquals(_refreshWindowRepository, repository)
                ? _refreshStartGeneration
                : _monitor.Generation;

        if (!_lifecycle.PublishBaseline(baselineRevision)) return;

        _displayedStatus = displayedStatus;
        _pendingStatus = null;
        // A coalesced refresh can still be running after baseline publication.
        _handledGeneration = Math.Max(_handledGeneration, startGeneration);
        RefreshRequirementChanged?.Invoke();

        var concurrent = _monitor.GetInvalidationsSince(startGeneration);
        Trace.WriteLine(
            $"Repository baseline published: revision={baselineRevision} startGeneration={startGeneration} currentGeneration={_monitor.Generation} concurrent={concurrent.HasAny}");
        if (concurrent.HasAny)
            HandleInvalidationBatch(concurrent);
    }

    public void RefreshStarted(Repository repository)
    {
        if (!_initialized || _disposed || !ReferenceEquals(repository, _monitoredRepository))
            return;

        _refreshWindowActive = true;
        _refreshWindowRepository = repository;
        _refreshStartGeneration = _monitor.Generation;
        Trace.WriteLine($"Repository refresh window started: generation={_refreshStartGeneration}");
    }

    public void RefreshCompleted(Repository repository)
    {
        if (!_initialized || _disposed || !_refreshWindowActive
            || !ReferenceEquals(repository, _refreshWindowRepository))
            return;

        var startGeneration = _refreshStartGeneration;
        _refreshWindowActive = false;
        _refreshWindowRepository = null;
        var invalidation = _monitor.GetInvalidationsSince(startGeneration);
        if (invalidation.HasAny)
            HandleInvalidationBatch(invalidation);

        StartStatusLoopIfNeeded();
    }

    // The desktop check delivers an actual captured callback after A -> B -> A.
    internal Action CapturePendingMonitorDeliveryForDesktopCheck()
    {
        if (!_initialized || _monitoredRepository is null)
            throw new InvalidOperationException("Repository monitoring is not initialized.");

        var owner = _monitoredRepository;
        var epoch = _deliveryEpoch;
        var batch = new RepositoryInvalidationBatch(
            _monitor.Generation + 1,
            hasWorkingTreeChanges: false,
            hasRelevantMetadataChanges: true,
            hasUnknownOrOverflow: false,
            metadataPaths: ["HEAD"]);
        return () => HandleInvalidationIfCurrent(owner, epoch, batch);
    }

    private void Monitor_RepositoryChanged(object? sender, RepositoryInvalidatedEventArgs args)
    {
        if (!_initialized || _disposed) return;

        var batch = args.Batch;
        var owner = _monitoredRepository;
        var epoch = _deliveryEpoch;
        Trace.WriteLine(
            $"Repository monitor invalidated: generation={batch.Generation} workingTree={batch.HasWorkingTreeChanges} metadata={batch.HasRelevantMetadataChanges} unknown={batch.HasUnknownOrOverflow} workingPaths={string.Join(",", batch.WorkingTreePaths)} metadataPaths={string.Join(",", batch.MetadataPaths)}");

        if (_dispatcher.HasThreadAccess)
            HandleInvalidationIfCurrent(owner, epoch, batch);
        else
            _dispatcher.TryEnqueue(() => HandleInvalidationIfCurrent(owner, epoch, batch));
    }

    private void HandleInvalidationIfCurrent(
        Repository? owner,
        long epoch,
        RepositoryInvalidationBatch batch)
    {
        if (!_initialized || _disposed
            || epoch != _deliveryEpoch
            || !ReferenceEquals(owner, _monitoredRepository))
            return;

        HandleInvalidationBatch(batch);
    }

    internal void HandleInvalidationBatch(RepositoryInvalidationBatch batch)
    {
        if (!_initialized || _disposed || !batch.HasAny
            || _monitoredRepository is null || _displayedStatus is null
            || _refreshWindowActive || batch.Generation <= _handledGeneration)
            return;

        _handledGeneration = batch.Generation;
        if (!_lifecycle.CanRunBackgroundStatusCheck) return;

        if (batch.HasUnknownOrOverflow)
        {
            LatchRefreshRequired("WatcherOverflow", batch.Generation);
            return;
        }

        if (batch.HasRelevantMetadataChanges)
        {
            LatchRefreshRequired(
                $"MetadataChanged:{batch.MetadataPaths.FirstOrDefault() ?? "<unknown>"}",
                batch.Generation);
            return;
        }

        if (!batch.HasWorkingTreeChanges) return;
        _pendingStatus = RepositoryInvalidationBatch.Merge(_pendingStatus, batch);
        StartStatusLoopIfNeeded();
    }

    private void StartStatusLoopIfNeeded()
    {
        if (!_initialized || _disposed || _refreshWindowActive
            || !_lifecycle.CanRunBackgroundStatusCheck
            || _pendingStatus is null || _statusRunning)
            return;

        _statusRunning = true;
        _ = RunStatusLoopAsync();
    }

    private async Task RunStatusLoopAsync()
    {
        try
        {
            while (_initialized && !_disposed && !_refreshWindowActive
                   && _lifecycle.CanRunBackgroundStatusCheck && _pendingStatus is not null)
            {
                var invalidation = _pendingStatus;
                _pendingStatus = null;

                var repository = _monitoredRepository;
                var baseline = _displayedStatus;
                var baselineRevision = _lifecycle.BaselineRevision;
                if (repository is null || baseline is null) continue;

                var statusToken = _repositoryStatusStop?.Token ?? _lifetimeStop.Token;
                WorkingTreeStatusSnapshot current;
                try
                {
                    current = await _statusReader.ReadAsync(repository, statusToken);
                }
                catch (OperationCanceledException) when (statusToken.IsCancellationRequested)
                {
                    if (!_initialized || _disposed) return;
                    continue;
                }
                catch (Exception exception)
                {
                    if (!_initialized || _disposed || _refreshWindowActive
                        || !ReferenceEquals(repository, _monitoredRepository)
                        || baselineRevision != _lifecycle.BaselineRevision)
                        continue;

                    Trace.WriteLine(
                        $"Working-tree change probe: generation={invalidation.Generation} result=error error={exception.GetType().Name}: {exception.Message}");
                    LatchRefreshRequired("StatusProbeFailed", invalidation.Generation);
                    return;
                }

                if (!_initialized || _disposed) return;
                if (_refreshWindowActive || !ReferenceEquals(repository, _monitoredRepository))
                    continue;

                var reason = WorkingTreeInvalidationEvaluator.Evaluate(baseline, current, invalidation);
                var disposition = _lifecycle.ApplyStatusCheckResult(
                    baselineRevision, reason != WorkingTreeInvalidationReason.None);

                if (disposition == RepositoryStatusCheckDisposition.StaleRevision)
                {
                    Trace.WriteLine(
                        $"Working-tree change probe: generation={invalidation.Generation} result=stale baselineRevision={baselineRevision} currentRevision={_lifecycle.BaselineRevision}");
                    continue;
                }

                if (disposition == RepositoryStatusCheckDisposition.IgnoredWhileLatched)
                    return;

                Trace.WriteLine(
                    $"Working-tree change probe: generation={invalidation.Generation} result={reason} baselineRevision={baselineRevision}");

                if (disposition == RepositoryStatusCheckDisposition.RefreshRequired)
                {
                    _pendingStatus = null;
                    RefreshRequirementChanged?.Invoke();
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            // All background failures are observed, even those outside the Git reader.
            Trace.WriteLine($"Working-tree status loop failed: {exception}");
            if (_initialized && !_disposed)
                LatchRefreshRequired("StatusLoopFailed", _monitor.Generation);
        }
        finally
        {
            _statusRunning = false;
            StartStatusLoopIfNeeded();
        }
    }

    private void LatchRefreshRequired(string reason, long generation)
    {
        var disposition = _lifecycle.Latch(_lifecycle.BaselineRevision);
        if (disposition == RepositoryStatusCheckDisposition.StaleRevision) return;

        _pendingStatus = null;
        Trace.WriteLine(
            $"RefreshRequired reason={reason} generation={generation} baselineRevision={_lifecycle.BaselineRevision}");
        RefreshRequirementChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initialized = false;
        _deliveryEpoch++;
        _pendingStatus = null;
        _refreshWindowActive = false;
        _refreshWindowRepository = null;
        _monitor.RepositoryChanged -= Monitor_RepositoryChanged;
        _lifetimeStop.Cancel();
        _repositoryStatusStop?.Cancel();
        _repositoryStatusStop?.Dispose();
        _repositoryStatusStop = null;
        _monitor.Dispose();
        _lifetimeStop.Dispose();
    }
}
