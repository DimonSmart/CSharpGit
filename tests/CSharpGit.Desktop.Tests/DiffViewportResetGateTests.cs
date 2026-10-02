using CSharpGit.Presentation.Controls;

namespace CSharpGit.Desktop.Tests;

public sealed class DiffViewportResetGateTests
{
    [Fact]
    public void NewSemanticContentInvalidatesOlderDeferredReset()
    {
        var gate = new DiffViewportResetGate();

        var first = gate.BeginReplacement();
        var second = gate.BeginReplacement();

        Assert.False(gate.IsCurrent(first));
        Assert.True(gate.IsCurrent(second));
    }

    [Fact]
    public void UserInteractionCancelsDeferredResetForCurrentContent()
    {
        var gate = new DiffViewportResetGate();
        var ticket = gate.BeginReplacement();

        gate.RegisterInteraction();

        Assert.False(gate.IsCurrent(ticket));
    }

    [Fact]
    public void ReplacementAfterInteractionGetsFreshResetTicket()
    {
        var gate = new DiffViewportResetGate();
        var old = gate.BeginReplacement();
        gate.RegisterInteraction();

        var current = gate.BeginReplacement();

        Assert.False(gate.IsCurrent(old));
        Assert.True(gate.IsCurrent(current));
    }
}
