namespace CSharpGit.Application.Abstractions;

public enum RepositoryCreationKind
{
    WorkingTree,
    BareShared
}

public interface IRepositoryCreationService
{
    Task CreateAsync(
        string path,
        RepositoryCreationKind kind,
        CancellationToken cancellationToken = default);
}
