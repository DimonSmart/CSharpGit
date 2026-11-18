namespace CSharpGit.Presentation.Views;

internal sealed record StartScreenActions(
    Func<Task> OpenRepository,
    Func<Task> CloneRepository,
    Func<Task> CreateRepository,
    Func<string, Task<bool>> SwitchRepository,
    Func<string, Task> ShowUnavailableRepository,
    Func<string, Task> OpenFolder,
    Func<string, Task> CopyPath,
    Action OpenSettings,
    Func<Task> ShowAbout);
