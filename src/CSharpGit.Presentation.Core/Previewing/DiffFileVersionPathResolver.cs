using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.Previewing;

internal sealed class DiffFileVersionPathResolver(
    IRepositoryFileVersionService fileVersionService,
    IRepositoryPathService repositoryPathService)
{
    internal async Task<string> ResolveAsync(
        Repository repository,
        DiffFileVersion version,
        DiffFileSide side,
        CancellationToken cancellationToken)
    {
        if (!version.CanOpen)
            throw new InvalidOperationException(version.UnavailableReason ?? "This file version is unavailable.");

        return version.Location switch
        {
            DiffFileVersionLocation.WorkingCopy =>
                repositoryPathService.ResolveExistingWorkingTreeFile(repository, version.GitPath),
            DiffFileVersionLocation.GitSnapshot =>
                (await fileVersionService.MaterializeAsync(repository, version, side, cancellationToken)).Path,
            _ => throw new InvalidOperationException(version.UnavailableReason ?? "This file version is unavailable.")
        };
    }
}
