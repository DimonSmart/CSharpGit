using CSharpGit.Presentation.Previewing;

namespace CSharpGit.Desktop.Tests;

public sealed class PreviewPublicationGateTests
{
    [Fact]
    public void NewSelectionInvalidatesPreviousPreview()
    {
        var gate = new PreviewPublicationGate<string>();
        var first = gate.Begin("repo|commit|A");
        var second = gate.Begin("repo|commit|B");

        Assert.False(gate.CanPublish(first, "repo|commit|A"));
        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.True(gate.CanPublish(second, "repo|commit|B"));
    }

    [Fact]
    public void ChangedCommitContextRejectsCompletedPreview()
    {
        var gate = new PreviewPublicationGate<string>();
        var lease = gate.Begin("repo|commit-a|file.txt");

        Assert.False(gate.CanPublish(lease, "repo|commit-b|file.txt"));
    }

    [Fact]
    public void LeavingSurfaceCancelsActivePreview()
    {
        var gate = new PreviewPublicationGate<string>();
        var lease = gate.Begin("repo|commit|file.txt");

        gate.Cancel();

        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.False(gate.CanPublish(lease, "repo|commit|file.txt"));
    }
}
