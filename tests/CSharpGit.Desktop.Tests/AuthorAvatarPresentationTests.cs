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
        var keyA = new AuthorAvatarLookupKey("a@example.com\nAuthor A", 1);
        var keyB = new AuthorAvatarLookupKey("b@example.com\nAuthor B", 1);

        var authorA = gate.Start(keyA);
        var authorB = gate.Start(keyB);

        Assert.True(authorA.CancellationToken.IsCancellationRequested);
        Assert.False(gate.IsCurrent(authorA, keyB));
        Assert.True(gate.IsCurrent(authorB, keyB));
        Assert.False(gate.IsCurrent(authorB, keyA));
    }

    [Fact]
    public void CancellationInvalidatesCurrentRequest()
    {
        using var gate = new AuthorAvatarRequestGate();
        var key = new AuthorAvatarLookupKey("a@example.com\nAuthor A", 1);
        var request = gate.Start(key);

        gate.Cancel();

        Assert.True(request.CancellationToken.IsCancellationRequested);
        Assert.False(gate.IsCurrent(request, key));
    }

    [Fact]
    public void CompletionDisposesRequestWithoutSemanticCancellation()
    {
        using var gate = new AuthorAvatarRequestGate();
        var key = new AuthorAvatarLookupKey("a@example.com\nAuthor A", 1);
        var request = gate.Start(key);

        Assert.True(gate.TryComplete(request));
        Assert.False(request.CancellationToken.IsCancellationRequested);
        Assert.False(gate.Cancel());
    }
}
