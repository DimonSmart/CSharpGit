using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class WindowCloseSafetyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EachProtectedOperationIndependentlyRejectsClose(int protectedOperation)
    {
        var active = new bool[7];
        active[protectedOperation] = true;

        var state = Create(active);

        Assert.True(state.IsUnsafeToClose);
        active[protectedOperation] = false;
        Assert.False(Create(active).IsUnsafeToClose);
    }

    [Fact]
    public void UnprotectedReadDoesNotBlockWindowClose()
    {
        Assert.False(Create(new bool[7]).IsUnsafeToClose);
    }

    [Fact]
    public void ClosingRemainsUnsafeUntilLastOverlappingOperationFinishes()
    {
        var active = new bool[7];
        active[0] = active[4] = active[5] = true;
        Assert.True(Create(active).IsUnsafeToClose);
        active[0] = false;
        Assert.True(Create(active).IsUnsafeToClose);
        active[4] = false;
        Assert.True(Create(active).IsUnsafeToClose);
        active[5] = false;
        Assert.False(Create(active).IsUnsafeToClose);
    }

    private static WindowCloseSafetyState Create(bool[] flags) => new(
        IsMutating: flags[0],
        IsChangingRepository: flags[1],
        IsHistoryRewriteRunning: flags[2],
        IsMaintenanceRunning: flags[3],
        IsUpdateRunning: flags[4],
        IsCloneRunning: flags[5],
        IsCreateRunning: flags[6]);
}
