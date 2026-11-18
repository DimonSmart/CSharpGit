namespace CSharpGit.Presentation.Views;

internal sealed record RepositorySelectorActions(
    Func<string, Task<bool>> SwitchRepository,
    Func<Task> OpenRepository,
    Func<Task> CloneRepository,
    Func<Task> CreateRepository,
    Func<Task<bool>> CloseRepository,
    Func<string, Task> OpenFolder,
    Func<string, Task> CopyPath,
    Func<string, string, Task> ShowError);
