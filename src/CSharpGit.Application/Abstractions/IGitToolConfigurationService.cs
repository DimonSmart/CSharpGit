using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IGitToolConfigurationService
{
    Task<GitToolConfigurationSnapshot> ReadAsync(
        Repository? repository,
        GitToolKind kind,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        Repository? repository,
        GitToolEdit edit,
        CancellationToken cancellationToken = default);

    Task RemoveOverrideAsync(
        Repository? repository,
        GitToolKind kind,
        GitToolWriteScope scope,
        CancellationToken cancellationToken = default);
}
