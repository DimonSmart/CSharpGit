using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public interface IConflictResolutionService
{
    Task ChooseConflictSideAsync(
        Repository repository,
        ConflictFile conflict,
        ConflictResolutionSide side,
        CancellationToken cancellationToken = default);

    Task KeepConflictDeletionAsync(
        Repository repository,
        ConflictFile conflict,
        CancellationToken cancellationToken = default);

    Task StageResolvedConflictAsync(
        Repository repository,
        ConflictFile conflict,
        CancellationToken cancellationToken = default);
}
