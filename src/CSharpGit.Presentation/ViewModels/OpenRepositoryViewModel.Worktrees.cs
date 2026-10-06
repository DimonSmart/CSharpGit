using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : IWorktreesRepositoryContext
{
    Task<bool> IWorktreesRepositoryContext.RunWorktreeMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation) =>
        MutateAsync(
            mutation,
            expectedRepository: expectedRepository);
}
