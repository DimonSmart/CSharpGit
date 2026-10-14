using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public interface ITagsRepositoryContext : INotifyPropertyChanged
{
    Repository? Repository { get; }
    bool IsBusy { get; }
    RepositoryOperation CurrentOperation { get; }
    IReadOnlyList<GitRemote> Remotes { get; }
    IReadOnlyList<GitBranch> LocalBranches { get; }

    Task<bool> RunTagMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string? errorContext,
        bool includeHistory);
}

public sealed record TagQueryResult<T>(T Value, string? ErrorMessage)
{
    public bool Succeeded => ErrorMessage is null;
}

public sealed record DeleteTagResult(
    bool Succeeded,
    bool RemoteWasMissing,
    string? RemoteName);

public sealed class TagsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ITagService _tagService;
    private readonly IReferenceService _referenceService;
    private ITagsRepositoryContext? _context;
    private GitTag? _selectedTag;
    private int _disposed;

    public TagsViewModel(
        ITagService tagService,
        IReferenceService referenceService)
    {
        _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
        _referenceService = referenceService ?? throw new ArgumentNullException(nameof(referenceService));
        CheckoutTagCommand = new AsyncCommand(
            CheckoutSelectedTagAsync,
            () => CanCheckoutSelectedTag);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GitTag> Items { get; } =
        new BulkObservableCollection<GitTag>();

    public ICommand CheckoutTagCommand { get; }

    public GitTag? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (ReferenceEquals(_selectedTag, value)) return;
            _selectedTag = value;
            Notify();
            NotifyAvailability();
        }
    }

    public bool CanMutate =>
        _context?.Repository is not null &&
        !_context.IsBusy &&
        _context.CurrentOperation == RepositoryOperation.None;

    public bool CanCheckoutSelectedTag =>
        CanMutate && SelectedTag is not null;

    internal void Attach(ITagsRepositoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (ReferenceEquals(_context, context)) return;

        Detach();
        _context = context;
        _context.PropertyChanged += Context_PropertyChanged;
        NotifyAvailability();
    }

    internal void Detach()
    {
        if (_context is not null)
            _context.PropertyChanged -= Context_PropertyChanged;
        _context = null;
        ClearRepositoryState();
        NotifyAvailability();
    }

    internal void ApplyRepositoryState(IEnumerable<GitTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var selectedName = SelectedTag?.Name;
        Replace(Items, tags);
        SelectedTag = FindTag(Items, selectedName);
        NotifyAvailability();
    }

    internal void ClearRepositoryState()
    {
        Replace(Items, []);
        SelectedTag = null;
        NotifyAvailability();
    }

    internal void RefreshAvailability() => NotifyAvailability();

    public GitRemote? PreferredRemote
    {
        get
        {
            var context = _context;
            if (context is null) return null;

            GitRemote? preferred = null;
            var upstream = context.LocalBranches.FirstOrDefault(branch => branch.IsCurrent)?.Upstream;
            if (!string.IsNullOrWhiteSpace(upstream))
            {
                var slash = upstream.IndexOf('/');
                var remoteName = slash > 0 ? upstream[..slash] : upstream;
                preferred = context.Remotes.FirstOrDefault(
                    remote => string.Equals(remote.Name, remoteName, StringComparison.Ordinal));
            }

            if (preferred is null && context.Remotes.Count == 1)
                preferred = context.Remotes[0];

            return preferred;
        }
    }

    public async Task<bool> CheckoutTagAsync(
        Repository repository,
        GitTag tag)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(tag);
        var context = _context;
        if (!CanRunMutation(context, repository)) return false;

        return await context!.RunTagMutationAsync(
            repository,
            () => _referenceService.CheckoutAsync(repository, tag.Name),
            errorContext: null,
            includeHistory: true);
    }

    public async Task<bool> CreateTagAsync(
        Repository repository,
        CreateTagRequest request)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        var context = _context;
        if (!CanRunMutation(context, repository)) return false;

        return await context!.RunTagMutationAsync(
            repository,
            () => _tagService.CreateTagAsync(repository, request),
            "Could not create tag",
            includeHistory: true);
    }

    public async Task<DeleteTagResult> DeleteTagAsync(
        Repository repository,
        GitTag tag,
        GitRemote? remote)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(tag);
        var context = _context;
        if (!CanRunMutation(context, repository))
            return new DeleteTagResult(false, false, remote?.Name);

        if (remote is null)
        {
            var localSucceeded = await context!.RunTagMutationAsync(
                repository,
                () => _tagService.DeleteTagAsync(repository, tag.Name),
                "Could not delete tag",
                includeHistory: true);
            return new DeleteTagResult(localSucceeded, false, null);
        }

        var remoteWasMissing = false;
        var succeeded = await context!.RunTagMutationAsync(
            repository,
            async () =>
            {
                var remoteTag = await _tagService.ReadRemoteTagAsync(repository, remote.Name, tag.Name);
                if (remoteTag is null)
                {
                    remoteWasMissing = true;
                    await _tagService.DeleteTagAsync(repository, tag.Name);
                    return;
                }

                if (!string.Equals(remoteTag.ObjectId, tag.ObjectId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"The remote tag '{tag.Name}' no longer matches the local tag.\n\n" +
                        $"Local object: {tag.ObjectId}\nRemote object: {remoteTag.ObjectId}\n\n" +
                        "Nothing was deleted. Refresh and review the tags before retrying.");

                await _tagService.DeleteRemoteTagAsync(repository, remoteTag);
                await _tagService.DeleteTagAsync(repository, tag.Name);
            },
            "Could not delete tag",
            includeHistory: true);

        return new DeleteTagResult(succeeded, succeeded && remoteWasMissing, remote.Name);
    }

    public async Task<PushTagResult?> PushTagAsync(
        Repository repository,
        GitTag tag,
        GitRemote remote)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(remote);
        var context = _context;
        if (!CanRunMutation(context, repository)) return null;

        PushTagResult? result = null;
        var succeeded = await context!.RunTagMutationAsync(
            repository,
            async () => result = await _tagService.PushTagAsync(repository, remote.Name, tag.Name),
            "Could not push tag",
            includeHistory: false);
        return succeeded ? result : null;
    }

    public async Task<bool> ForceUpdateRemoteTagAsync(
        Repository repository,
        RemoteTagConflictSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(snapshot);
        var context = _context;
        if (!CanRunMutation(context, repository)) return false;

        return await context!.RunTagMutationAsync(
            repository,
            () => _tagService.ForceUpdateRemoteTagAsync(repository, snapshot),
            "Could not force update remote tag",
            includeHistory: false);
    }

    public async Task<TagQueryResult<RemoteTagInfo?>> ReadRemoteTagAsync(
        Repository repository,
        GitRemote remote,
        string tagName)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        var context = _context;
        if (!IsCurrentRepository(context, repository))
            return new TagQueryResult<RemoteTagInfo?>(null, "The repository changed while the tag dialog was open.");

        try
        {
            var tag = await _tagService.ReadRemoteTagAsync(repository, remote.Name, tagName);
            return IsCurrentRepository(context, repository)
                ? new TagQueryResult<RemoteTagInfo?>(tag, null)
                : new TagQueryResult<RemoteTagInfo?>(null, "The repository changed while reading remote tags.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new TagQueryResult<RemoteTagInfo?>(null, exception.Message);
        }
    }

    public async Task<TagQueryResult<IReadOnlyList<RemoteTagInfo>>> ReadRemoteTagsAsync(
        Repository repository,
        GitRemote remote)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(remote);
        var context = _context;
        if (!IsCurrentRepository(context, repository))
            return new TagQueryResult<IReadOnlyList<RemoteTagInfo>>([], "The repository changed while the tag dialog was open.");

        try
        {
            var tags = await _tagService.ReadRemoteTagsAsync(repository, remote.Name);
            return IsCurrentRepository(context, repository)
                ? new TagQueryResult<IReadOnlyList<RemoteTagInfo>>(tags, null)
                : new TagQueryResult<IReadOnlyList<RemoteTagInfo>>([], "The repository changed while reading remote tags.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new TagQueryResult<IReadOnlyList<RemoteTagInfo>>([], exception.Message);
        }
    }

    public async Task<bool> DeleteRemoteTagAsync(
        Repository repository,
        RemoteTagInfo remoteTag)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(remoteTag);
        var context = _context;
        if (!CanRunMutation(context, repository)) return false;

        return await context!.RunTagMutationAsync(
            repository,
            () => _tagService.DeleteRemoteTagAsync(repository, remoteTag),
            "Could not delete remote tag",
            includeHistory: false);
    }

    public async Task<bool> FetchTagsAsync(
        Repository repository,
        GitRemote remote)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(remote);
        var context = _context;
        if (!CanRunMutation(context, repository)) return false;

        return await context!.RunTagMutationAsync(
            repository,
            () => _tagService.FetchTagsAsync(repository, remote.Name),
            "Could not fetch tags",
            includeHistory: true);
    }

    public async Task<bool> PushAllTagsAsync(
        Repository repository,
        GitRemote remote)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(remote);
        var context = _context;
        if (!CanRunMutation(context, repository)) return false;

        return await context!.RunTagMutationAsync(
            repository,
            () => _tagService.PushAllTagsAsync(repository, remote.Name),
            "Could not push all tags",
            includeHistory: false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Detach();
    }

    private Task CheckoutSelectedTagAsync()
    {
        var repository = _context?.Repository;
        var tag = SelectedTag;
        return repository is null || tag is null
            ? Task.CompletedTask
            : CheckoutTagAsync(repository, tag);
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ITagsRepositoryContext.Repository))
        {
            ClearRepositoryState();
            return;
        }

        if (args.PropertyName is nameof(ITagsRepositoryContext.IsBusy)
            or nameof(ITagsRepositoryContext.CurrentOperation))
            NotifyAvailability();
    }

    private void NotifyAvailability()
    {
        Notify(nameof(CanMutate));
        Notify(nameof(CanCheckoutSelectedTag));
        ((AsyncCommand)CheckoutTagCommand).RaiseCanExecuteChanged();
    }

    private bool CanRunMutation(
        ITagsRepositoryContext? context,
        Repository expectedRepository) =>
        CanMutate && IsCurrentRepository(context, expectedRepository);

    private static bool IsCurrentRepository(
        ITagsRepositoryContext? context,
        Repository expectedRepository) =>
        context is not null && ReferenceEquals(context.Repository, expectedRepository);

    private static GitTag? FindTag(
        IEnumerable<GitTag> tags,
        string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : tags.FirstOrDefault(
                tag => string.Equals(tag.Name, name, StringComparison.Ordinal));

    private static void Replace<T>(
        ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        var snapshot = values as IReadOnlyList<T> ?? values.ToArray();
        if (target.SequenceEqual(snapshot)) return;

        if (target is BulkObservableCollection<T> bulk)
        {
            bulk.ReplaceAll(snapshot);
            return;
        }

        target.Clear();
        foreach (var value in snapshot)
            target.Add(value);
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
