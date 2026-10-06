using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

internal interface ITagsRepositoryContext
{
    Repository? Repository { get; }
    bool IsBusy { get; }
    RepositoryOperation CurrentOperation { get; }
    IReadOnlyList<GitRemote> Remotes { get; }
    IReadOnlyList<GitBranch> LocalBranches { get; }

    Task<bool> RunTagMutationAsync(
        Func<Task> mutation,
        string errorContext,
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

public sealed class TagsViewModel
{
    private readonly ITagService _tagService;
    private ITagsRepositoryContext? _context;

    public TagsViewModel(ITagService tagService)
    {
        _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
    }

    internal void Attach(ITagsRepositoryContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public bool CanMutate =>
        _context?.Repository is not null &&
        !_context.IsBusy &&
        _context.CurrentOperation == RepositoryOperation.None;

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

    public async Task<bool> CreateTagAsync(CreateTagRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null || !CanMutate) return false;

        return await context.RunTagMutationAsync(
            () => _tagService.CreateTagAsync(repository, request),
            "Could not create tag",
            includeHistory: true);
    }

    public async Task<DeleteTagResult> DeleteTagAsync(GitTag tag, GitRemote? remote)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null || !CanMutate)
            return new DeleteTagResult(false, false, remote?.Name);

        if (remote is null)
        {
            var localSucceeded = await context.RunTagMutationAsync(
                () => _tagService.DeleteTagAsync(repository, tag.Name),
                "Could not delete tag",
                includeHistory: true);
            return new DeleteTagResult(localSucceeded, false, null);
        }

        var remoteWasMissing = false;
        var succeeded = await context.RunTagMutationAsync(
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

    public async Task<PushTagResult?> PushTagAsync(GitTag tag, GitRemote remote)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(remote);
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null || !CanMutate) return null;

        PushTagResult? result = null;
        var succeeded = await context.RunTagMutationAsync(
            async () => result = await _tagService.PushTagAsync(repository, remote.Name, tag.Name),
            "Could not push tag",
            includeHistory: false);
        return succeeded ? result : null;
    }

    public async Task<bool> ForceUpdateRemoteTagAsync(RemoteTagConflictSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null || !CanMutate) return false;

        return await context.RunTagMutationAsync(
            () => _tagService.ForceUpdateRemoteTagAsync(repository, snapshot),
            "Could not force update remote tag",
            includeHistory: false);
    }

    public async Task<TagQueryResult<RemoteTagInfo?>> ReadRemoteTagAsync(
        GitRemote remote,
        string tagName)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        var repository = _context?.Repository;
        if (repository is null)
            return new TagQueryResult<RemoteTagInfo?>(null, "No repository is open.");

        try
        {
            var tag = await _tagService.ReadRemoteTagAsync(repository, remote.Name, tagName);
            return new TagQueryResult<RemoteTagInfo?>(tag, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new TagQueryResult<RemoteTagInfo?>(null, exception.Message);
        }
    }

    public async Task<TagQueryResult<IReadOnlyList<RemoteTagInfo>>> ReadRemoteTagsAsync(
        GitRemote remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        var repository = _context?.Repository;
        if (repository is null)
            return new TagQueryResult<IReadOnlyList<RemoteTagInfo>>([], "No repository is open.");

        try
        {
            var tags = await _tagService.ReadRemoteTagsAsync(repository, remote.Name);
            return new TagQueryResult<IReadOnlyList<RemoteTagInfo>>(tags, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new TagQueryResult<IReadOnlyList<RemoteTagInfo>>([], exception.Message);
        }
    }

    public async Task<bool> DeleteRemoteTagAsync(RemoteTagInfo remoteTag)
    {
        ArgumentNullException.ThrowIfNull(remoteTag);
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null || !CanMutate) return false;

        return await context.RunTagMutationAsync(
            () => _tagService.DeleteRemoteTagAsync(repository, remoteTag),
            "Could not delete remote tag",
            includeHistory: false);
    }

    public async Task<bool> FetchTagsAsync(GitRemote remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null || !CanMutate) return false;

        return await context.RunTagMutationAsync(
            () => _tagService.FetchTagsAsync(repository, remote.Name),
            "Could not fetch tags",
            includeHistory: true);
    }

    public async Task<bool> PushAllTagsAsync(GitRemote remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        var context = _context;
        var repository = context?.Repository;
        if (context is null || repository is null || !CanMutate) return false;

        return await context.RunTagMutationAsync(
            () => _tagService.PushAllTagsAsync(repository, remote.Name),
            "Could not push all tags",
            includeHistory: false);
    }
}
