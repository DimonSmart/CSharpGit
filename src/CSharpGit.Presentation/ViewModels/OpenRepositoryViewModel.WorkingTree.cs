using CSharpGit.Domain;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel : IWorkingTreeRepositoryContext
{
    bool IWorkingTreeRepositoryContext.CanRunRepositoryMutation => CanMutate();
    bool IWorkingTreeRepositoryContext.CanRunWorkingTreeMutation => CanBulkMutate();

    Task<bool> IWorkingTreeRepositoryContext.RunWorkingTreeMutationAsync(
        Repository expectedRepository,
        Func<Task> mutation,
        string errorContext,
        Action? beforeMutation) =>
        MutateAsync(
            mutation,
            errorContext,
            beforeMutation,
            includeHistory: false,
            expectedRepository: expectedRepository);

    void IWorkingTreeRepositoryContext.ReportWorkingTreeError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ErrorMessage = string.IsNullOrWhiteSpace(ErrorMessage)
            ? message
            : $"{message}{Environment.NewLine}{ErrorMessage}";
    }
}
