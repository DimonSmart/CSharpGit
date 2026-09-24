using CSharpGit.Presentation.Controls;

namespace CSharpGit.Desktop.Tests;

public sealed class AuthorAvatarPresentationTests
{
    [Theory]
    [InlineData("John Doe", "john@example.com", "JD")]
    [InlineData("John", "john@example.com", "J")]
    [InlineData("John M. Doe", "john@example.com", "JD")]
    [InlineData("Dmitry Dorogoy", "dmitry@example.com", "DD")]
    [InlineData("", "john@example.com", "J")]
    [InlineData("", "", "?")]
    public void InitialsFallbackIsStableAndLocal(
        string authorName,
        string authorEmail,
        string expected) =>
        Assert.Equal(expected, AuthorAvatarFallback.GetInitials(authorName, authorEmail));

    [Fact]
    public void StableIdentityUsesStableColorIndex()
    {
        var first = AuthorAvatarFallback.GetStableColorIndex("John Doe", " John@Example.com ", 8);
        var second = AuthorAvatarFallback.GetStableColorIndex("Other Name", "john@example.com", 8);

        Assert.Equal(first, second);
        Assert.InRange(first, 0, 7);
    }

    [Fact]
    public void RecyclingGateRejectsLateResultForPreviousIdentity()
    {
        using var gate = new AuthorAvatarRequestGate();

        var authorA = gate.Start("a@example.com\nAuthor A");
        var authorB = gate.Start("b@example.com\nAuthor B");

        Assert.True(authorA.CancellationToken.IsCancellationRequested);
        Assert.False(gate.IsCurrent(authorA, "b@example.com\nAuthor B"));
        Assert.True(gate.IsCurrent(authorB, "b@example.com\nAuthor B"));
        Assert.False(gate.IsCurrent(authorB, "a@example.com\nAuthor A"));
    }

    [Fact]
    public void CancellationInvalidatesCurrentRequest()
    {
        using var gate = new AuthorAvatarRequestGate();
        var request = gate.Start("a@example.com\nAuthor A");

        gate.Cancel();

        Assert.True(request.CancellationToken.IsCancellationRequested);
        Assert.False(gate.IsCurrent(request, "a@example.com\nAuthor A"));
    }
}
