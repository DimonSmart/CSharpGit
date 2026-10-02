using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class DisplayedWorkingTreeBaselineTests
{
    [Fact]
    public void PublishingIdenticalSnapshotStillAdvancesRevision()
    {
        var baseline = new DisplayedWorkingTreeBaseline();
        var snapshot = Snapshot("? a.txt");

        Assert.True(baseline.Publish(snapshot));
        Assert.Equal(1, baseline.Revision);

        Assert.False(baseline.Publish(Snapshot("? a.txt")));

        Assert.Equal(2, baseline.Revision);
        Assert.Equal(snapshot, baseline.Snapshot);
    }

    [Fact]
    public void ClearDoesNotAdvanceRevision()
    {
        var baseline = new DisplayedWorkingTreeBaseline();
        baseline.Publish(Snapshot("? a.txt"));

        Assert.True(baseline.Clear());

        Assert.Null(baseline.Snapshot);
        Assert.Equal(1, baseline.Revision);

        baseline.Publish(Snapshot("? a.txt"));
        Assert.Equal(2, baseline.Revision);
    }

    private static WorkingTreeStatusSnapshot Snapshot(string record) =>
        new([new WorkingTreeStatusEntry("a.txt", record)]);
}
