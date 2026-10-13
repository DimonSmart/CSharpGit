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
    public void AbaSelectionKeepsGenerationsDistinct()
    {
        var gate = new PreviewPublicationGate<string>();
        var firstA = gate.Begin("repo|X|A");
        var b = gate.Begin("repo|X|B");
        var secondA = gate.Begin("repo|X|A");

        Assert.False(gate.CanPublish(firstA, "repo|X|A"));
        Assert.False(gate.CanPublish(b, "repo|X|B"));
        Assert.True(gate.CanPublish(secondA, "repo|X|A"));
    }

    [Fact]
    public async Task ReverseOrderCompletionsOnlyPublishLatestLease()
    {
        var gate = new PreviewPublicationGate<string>();
        var firstFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<string>();

        async Task CompleteAsync(string path, Task wait)
        {
            var lease = gate.Begin(path);
            await wait;
            if (gate.CanPublish(lease, path))
                published.Add(path);
        }

        var first = CompleteAsync("repo|X|A", firstFinished.Task);
        var second = CompleteAsync("repo|X|B", secondFinished.Task);
        secondFinished.SetResult();
        await second;
        firstFinished.SetResult();
        await first;

        Assert.Equal(["repo|X|B"], published);
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
