namespace CSharpGit.Presentation.Controls;

internal sealed class DiffViewportResetGate
{
    private long _contentGeneration;
    private long _interactionGeneration;

    public DiffViewportResetTicket BeginReplacement() =>
        new(++_contentGeneration, _interactionGeneration);

    public void RegisterInteraction() => _interactionGeneration++;

    public bool IsCurrent(DiffViewportResetTicket ticket) =>
        ticket.ContentGeneration == _contentGeneration &&
        ticket.InteractionGeneration == _interactionGeneration;
}

internal readonly record struct DiffViewportResetTicket(
    long ContentGeneration,
    long InteractionGeneration);
