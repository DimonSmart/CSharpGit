namespace CSharpGit.Presentation.Threading;

public interface IUiDispatcher
{
    bool HasThreadAccess { get; }

    bool TryEnqueue(Action action);
}
