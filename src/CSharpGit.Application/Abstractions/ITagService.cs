using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface ITagService
{
    Task<IReadOnlyList<GitTag>> ReadTagsAsync(
        Repository repository,
        CancellationToken cancellationToken = default);

    Task CreateTagAsync(
        Repository repository,
        CreateTagRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteTagAsync(
        Repository repository,
        string tagName,
        CancellationToken cancellationToken = default);

    Task FetchTagsAsync(
        Repository repository,
        string remote,
        CancellationToken cancellationToken = default);

    Task<PushTagResult> PushTagAsync(
        Repository repository,
        string remote,
        string tagName,
        CancellationToken cancellationToken = default);

    Task PushAllTagsAsync(
        Repository repository,
        string remote,
        CancellationToken cancellationToken = default);

    Task DeleteRemoteTagAsync(
        Repository repository,
        RemoteTagInfo expectedTag,
        CancellationToken cancellationToken = default);

    Task<RemoteTagInfo?> ReadRemoteTagAsync(
        Repository repository,
        string remote,
        string tagName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteTagInfo>> ReadRemoteTagsAsync(
        Repository repository,
        string remote,
        CancellationToken cancellationToken = default);

    Task ForceUpdateRemoteTagAsync(
        Repository repository,
        RemoteTagConflictSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
