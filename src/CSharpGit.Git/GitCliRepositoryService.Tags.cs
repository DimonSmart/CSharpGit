using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed partial class GitCliRepositoryService
{
    private GitTagService Tags => new(_executor);

    public Task<IReadOnlyList<GitTag>> ReadTagsAsync(
        Repository repository,
        CancellationToken cancellationToken = default) =>
        Tags.ReadTagsAsync(repository, cancellationToken);

    public Task CreateTagAsync(
        Repository repository,
        CreateTagRequest request,
        CancellationToken cancellationToken = default) =>
        Tags.CreateTagAsync(repository, request, cancellationToken);

    public Task DeleteTagAsync(
        Repository repository,
        string tagName,
        CancellationToken cancellationToken = default) =>
        Tags.DeleteTagAsync(repository, tagName, cancellationToken);

    public Task FetchTagsAsync(
        Repository repository,
        string remote,
        CancellationToken cancellationToken = default) =>
        Tags.FetchTagsAsync(repository, remote, cancellationToken);

    public Task<PushTagResult> PushTagAsync(
        Repository repository,
        string remote,
        string tagName,
        CancellationToken cancellationToken = default) =>
        Tags.PushTagAsync(repository, remote, tagName, cancellationToken);

    public Task PushAllTagsAsync(
        Repository repository,
        string remote,
        CancellationToken cancellationToken = default) =>
        Tags.PushAllTagsAsync(repository, remote, cancellationToken);

    public Task DeleteRemoteTagAsync(
        Repository repository,
        string remote,
        string tagName,
        CancellationToken cancellationToken = default) =>
        Tags.DeleteRemoteTagAsync(repository, remote, tagName, cancellationToken);

    public Task<RemoteTagInfo?> ReadRemoteTagAsync(
        Repository repository,
        string remote,
        string tagName,
        CancellationToken cancellationToken = default) =>
        Tags.ReadRemoteTagAsync(repository, remote, tagName, cancellationToken);

    public Task ForceUpdateRemoteTagAsync(
        Repository repository,
        RemoteTagConflictSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        Tags.ForceUpdateRemoteTagAsync(repository, snapshot, cancellationToken);
}
