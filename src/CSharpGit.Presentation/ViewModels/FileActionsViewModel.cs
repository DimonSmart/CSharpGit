using System.ComponentModel;
using System.Runtime.CompilerServices;
using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Previewing;
using CSharpGit.Presentation.Threading;

namespace CSharpGit.Presentation.ViewModels;

public abstract record FileActionRequest(Repository Repository);
public sealed record CommitFileActionRequest(Repository Repository, string CommitHash, string Path)
    : FileActionRequest(Repository);
public sealed record WorkingTreeFileActionRequest(Repository Repository, WorkingTreeChange Change, WorkingTreeDiffKind Kind)
    : FileActionRequest(Repository);

public enum FileActionKind { Original, Changed, Preferred, ExternalDiff, Reveal }
public enum FileActionOutcomeKind { Completed, Unavailable, NotConfigured, Stale, Cancelled, Failed }
public enum FileActionLoadPhase { Unavailable, Loading, Ready, Failed }

public sealed record FileActionOutcome(FileActionOutcomeKind Kind, string? Reason = null);
public sealed record PreparedFileActions(FileActionRequest Request, DiffFileVersionPair Pair, string? RevealPath)
{
    public bool CanOpenOriginal => Pair.Original.CanOpen;
    public bool CanOpenChanged => Pair.Changed.CanOpen;
    public bool CanExternalDiff => FileActionsViewModel.CanExternalDiff(Pair);
    public bool CanReveal => RevealPath is not null;
}

public sealed record FileActionSelectionState(
    FileActionLoadPhase Phase,
    FileActionRequest? Request,
    PreparedFileActions? Prepared,
    string? Error = null,
    string? RevealPath = null);

public sealed class FileActionsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IRepositoryFileVersionService _versions;
    private readonly IRepositoryPathService _paths;
    private readonly IDesktopShellService _shell;
    private readonly IExternalGitToolService _tools;
    private readonly DiffFileVersionPathResolver _pathResolver;
    private readonly IUiDispatcher _dispatcher;
    private IFileActionsContext? _context;
    private long _commitGeneration;
    private long _workingGeneration;
    private bool _disposed;

    public FileActionsViewModel(
        IRepositoryFileVersionService versions,
        IRepositoryPathService paths,
        IDesktopShellService shell,
        IExternalGitToolService tools,
        IUiDispatcher dispatcher)
    {
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _pathResolver = new DiffFileVersionPathResolver(versions, paths);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public FileActionSelectionState Commit { get; private set; } =
        new(FileActionLoadPhase.Unavailable, null, null);
    public FileActionSelectionState WorkingTree { get; private set; } =
        new(FileActionLoadPhase.Unavailable, null, null);
    public string RevealDescription => _shell.RevealDescription;
    internal DiffFileVersionPathResolver PathResolver => _pathResolver;

    internal void Attach(IFileActionsContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public bool IsCurrent(FileActionRequest request) =>
        !_disposed && _context?.IsCurrentFileActionsRepository(request.Repository) == true;

    public void Invalidate()
    {
        Interlocked.Increment(ref _commitGeneration);
        Interlocked.Increment(ref _workingGeneration);
        Commit = new(FileActionLoadPhase.Unavailable, null, null);
        WorkingTree = new(FileActionLoadPhase.Unavailable, null, null);
        Notify(nameof(Commit));
        Notify(nameof(WorkingTree));
    }

    // Commit actions remain lazy until explicitly requested, as in the original UI.
    public void SelectCommitLazy(CommitFileActionRequest? request)
    {
        Interlocked.Increment(ref _commitGeneration);
        SetState(false, request is null || !IsCurrent(request)
            ? new(FileActionLoadPhase.Unavailable, null, null)
            : new(FileActionLoadPhase.Loading, request, null,
                RevealPath: TryResolveReveal(request.Repository, request.Path)));
    }

    public Task SelectCommitAsync(CommitFileActionRequest? request) =>
        SelectAsync(request, workingTree: false);

    public Task SelectWorkingTreeAsync(WorkingTreeFileActionRequest? request) =>
        SelectAsync(request, workingTree: true);

    private async Task SelectAsync(FileActionRequest? request, bool workingTree)
    {
        var generation = workingTree
            ? Interlocked.Increment(ref _workingGeneration)
            : Interlocked.Increment(ref _commitGeneration);
        SetState(workingTree, new(request is null ? FileActionLoadPhase.Unavailable : FileActionLoadPhase.Loading, request, null));
        if (request is null || !IsCurrent(request)) return;

        try
        {
            var prepared = await PrepareAsync(request);
            if (IsSelectedCurrent(workingTree, generation, request))
                SetState(workingTree, prepared is null
                    ? new(FileActionLoadPhase.Unavailable, request, null)
                    : new(FileActionLoadPhase.Ready, request, prepared));
        }
        catch (OperationCanceledException)
        {
            if (IsSelectedCurrent(workingTree, generation, request))
                SetState(workingTree, new(FileActionLoadPhase.Unavailable, request, null));
        }
        catch (Exception exception)
        {
            if (IsSelectedCurrent(workingTree, generation, request))
                SetState(workingTree, new(FileActionLoadPhase.Failed, request, null, UserFacingFileError(exception)));
        }
    }

    private bool IsSelectedCurrent(bool workingTree, long generation, FileActionRequest request)
    {
        var state = workingTree ? WorkingTree : Commit;
        return !_disposed && IsCurrent(request)
            && generation == (workingTree ? Volatile.Read(ref _workingGeneration) : Volatile.Read(ref _commitGeneration))
            && Equals(state.Request, request);
    }

    private void SetState(bool workingTree, FileActionSelectionState state)
    {
        if (_disposed) return;
        if (workingTree)
        {
            WorkingTree = state;
            Notify(nameof(WorkingTree));
        }
        else
        {
            Commit = state;
            Notify(nameof(Commit));
        }
    }

    // Commit image preview may already have prepared the same pair.
    public void PublishCommitPreview(CommitFileActionRequest request, DiffFileVersionPair pair)
    {
        if (!IsCurrent(request) || !Equals(Commit.Request, request))
            return;
        SetState(false, new(FileActionLoadPhase.Ready, request,
            new PreparedFileActions(request, pair, TryResolveReveal(request.Repository, pair.RevealPath))));
    }

    public async Task OpenResolvedVersionAsync(
        Repository repository, DiffFileVersion version, DiffFileSide side)
    {
        if (!IsCurrent(new CommitFileActionRequest(repository, string.Empty, version.GitPath)))
            return;
        var path = await _pathResolver.ResolveAsync(repository, version, side, CancellationToken.None);
        if (_context?.IsCurrentFileActionsRepository(repository) == true)
            await _shell.OpenFileAsync(path);
    }

    // Image preview may already have resolved the same version pair; never overwrite a new selection.
    public void PublishWorkingTreePreview(WorkingTreeFileActionRequest request, DiffFileVersionPair pair)
    {
        if (!IsCurrent(request) || !Equals(WorkingTree.Request, request))
            return;
        SetState(true, new(FileActionLoadPhase.Ready, request,
            new PreparedFileActions(request, pair, TryResolveReveal(request.Repository, pair.RevealPath))));
    }

    // Menu preparation is deliberately not published to either selection slot.
    public async Task<PreparedFileActions?> PrepareAsync(FileActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsCurrent(request)) return null;
        using var lease = _context!.AcquireFileActionsRead(request.Repository);
        if (lease is null) return null;
        var pair = await ResolveAsync(request, lease.Token);
        if (!IsCurrent(request)) return null;
        return new PreparedFileActions(request, pair, TryResolveReveal(request.Repository, pair.RevealPath));
    }

    public async Task<FileActionOutcome> ExecuteAsync(
        FileActionRequest request,
        FileActionKind action,
        PreparedFileActions? prepared = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsCurrent(request)) return new(FileActionOutcomeKind.Stale);
        try
        {
            // A mutable working tree is re-resolved for the same captured request, never the current selection.
            if (prepared is null || !Equals(prepared.Request, request) || request is WorkingTreeFileActionRequest)
                prepared = await PrepareAsync(request);

            if (prepared is null || !IsCurrent(request))
                return new(FileActionOutcomeKind.Stale);

            if (action is FileActionKind.Original or FileActionKind.Changed or FileActionKind.Preferred)
            {
                var side = action == FileActionKind.Preferred
                    ? prepared.Pair.Changed.CanOpen ? DiffFileSide.Changed : DiffFileSide.Original
                    : action == FileActionKind.Original ? DiffFileSide.Original : DiffFileSide.Changed;
                var version = side == DiffFileSide.Original ? prepared.Pair.Original : prepared.Pair.Changed;
                if (!version.CanOpen)
                    return new(FileActionOutcomeKind.Unavailable, version.UnavailableReason);
                var path = await _pathResolver.ResolveAsync(request.Repository, version, side, CancellationToken.None);
                if (!IsCurrent(request)) return new(FileActionOutcomeKind.Stale);
                await _shell.OpenFileAsync(path);
            }
            else if (action == FileActionKind.ExternalDiff)
            {
                if (!CanExternalDiff(prepared.Pair))
                    return new(FileActionOutcomeKind.Unavailable, ExternalDiffUnavailableReason(prepared.Pair));
                if (!IsCurrent(request)) return new(FileActionOutcomeKind.Stale);
                await _tools.RunExternalDiffAsync(request.Repository, prepared.Pair);
            }
            else if (action == FileActionKind.Reveal)
            {
                // Do not trust a cached absolute path; validate the actual tree at execution time.
                var path = _paths.ResolveExistingWorkingTreeFile(
                    request.Repository, prepared.Pair.RevealPath, allowFinalLink: true);
                if (!IsCurrent(request)) return new(FileActionOutcomeKind.Stale);
                await _shell.RevealFileAsync(path);
            }
            return new(FileActionOutcomeKind.Completed);
        }
        catch (OperationCanceledException)
        {
            return new(FileActionOutcomeKind.Cancelled);
        }
        catch (GitToolNotConfiguredException)
        {
            return new(FileActionOutcomeKind.NotConfigured);
        }
        catch (Exception exception)
        {
            return new(FileActionOutcomeKind.Failed, UserFacingFileError(exception));
        }
    }

    private Task<DiffFileVersionPair> ResolveAsync(FileActionRequest request, CancellationToken token) =>
        request switch
        {
            CommitFileActionRequest commit => _versions.ResolveCommitAsync(
                commit.Repository, commit.CommitHash, commit.Path, token),
            WorkingTreeFileActionRequest change => _versions.ResolveWorkingTreeAsync(
                change.Repository, change.Change, change.Kind, token),
            _ => throw new NotSupportedException("Unsupported file action request.")
        };

    public static bool CanExternalDiff(DiffFileVersionPair pair) =>
        CanExternalDiffSide(pair.Original) && CanExternalDiffSide(pair.Changed);

    private static bool CanExternalDiffSide(DiffFileVersion version) =>
        version.EntryKind == GitEntryKind.RegularFile && version.Location != DiffFileVersionLocation.Unavailable
        || version.EntryKind == GitEntryKind.Missing && version.Location == DiffFileVersionLocation.Unavailable;

    public static string ExternalDiffUnavailableReason(DiffFileVersionPair? pair)
    {
        if (pair is null) return "Select a file diff first.";
        if (!CanExternalDiffSide(pair.Original))
            return pair.Original.UnavailableReason ?? "The original side cannot be represented for an external diff tool.";
        if (!CanExternalDiffSide(pair.Changed))
            return pair.Changed.UnavailableReason ?? "The changed side cannot be represented for an external diff tool.";
        return "External diff is unavailable.";
    }

    public string? TryResolveReveal(Repository repository, string gitPath)
    {
        try
        {
            return _paths.ResolveExistingWorkingTreeFile(repository, gitPath, allowFinalLink: true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string UserFacingFileError(Exception exception) => exception switch
    {
        FileNotFoundException => "The file is no longer present in the current working tree.",
        UnauthorizedAccessException => "CSharpGit does not have permission to access this file.",
        NotSupportedException => exception.Message,
        ArgumentException => exception.Message,
        InvalidOperationException => exception.Message,
        IOException => "The file could not be prepared or opened.",
        _ => "The file operation failed."
    };

    private void Notify([CallerMemberName] string? name = null)
    {
        if (_disposed) return;
        void Raise() { if (!_disposed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
        if (_dispatcher.HasThreadAccess) Raise();
        else _dispatcher.TryEnqueue(Raise);
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Increment(ref _commitGeneration);
        Interlocked.Increment(ref _workingGeneration);
    }
}
