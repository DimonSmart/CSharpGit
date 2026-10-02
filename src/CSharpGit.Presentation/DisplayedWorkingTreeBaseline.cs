using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.ViewModels;

internal sealed class DisplayedWorkingTreeBaseline
{
    public WorkingTreeStatusSnapshot? Snapshot { get; private set; }

    public long Revision { get; private set; }

    public bool Publish(WorkingTreeStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var changed = !Equals(Snapshot, snapshot);
        Snapshot = snapshot;
        Revision++;
        return changed;
    }

    public bool Clear()
    {
        if (Snapshot is null) return false;

        Snapshot = null;
        return true;
    }
}
