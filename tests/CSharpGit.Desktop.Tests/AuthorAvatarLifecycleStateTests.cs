using CSharpGit.Presentation.Controls;

namespace CSharpGit.Desktop.Tests;

public sealed class AuthorAvatarLifecycleStateTests
{
    private static readonly AuthorAvatarEffectiveState EnabledA =
        new("a@example.com\nAuthor A", true, true, 1);

    [Fact]
    public void SameIdentitySurvivesLoadUnloadLoadWithoutSecondResolve()
    {
        var state = new AuthorAvatarLifecycleState();
        Assert.True(state.Apply(EnabledA).Changed);
        Assert.True(state.TryStartResolve(isLoaded: true));
        Assert.True(state.TrySetResolvedImage(EnabledA.LookupKey));

        Assert.False(state.Apply(EnabledA).Changed);
        Assert.False(state.TryStartResolve(isLoaded: false));
        Assert.False(state.TryStartResolve(isLoaded: true));
        Assert.True(state.IsResolveDeduplicated(isLoaded: true));
        Assert.Equal(AuthorAvatarResolutionStatus.ResolvedImage, state.ResolutionStatus);
    }

    [Fact]
    public void PendingRequestIsDeduplicated()
    {
        var state = new AuthorAvatarLifecycleState();
        state.Apply(EnabledA);

        Assert.True(state.TryStartResolve(isLoaded: true));
        Assert.False(state.TryStartResolve(isLoaded: true));
        Assert.True(state.IsResolveDeduplicated(isLoaded: true));
        Assert.Equal(AuthorAvatarResolutionStatus.Pending, state.ResolutionStatus);
    }

    [Fact]
    public void EquivalentStateDoesNotCreateTransition()
    {
        var state = new AuthorAvatarLifecycleState();

        Assert.True(state.Apply(EnabledA).Changed);
        Assert.False(state.Apply(EnabledA).Changed);
    }

    [Fact]
    public void RecyclingInvalidatesPendingIdentityExactlyAtStateTransition()
    {
        var state = new AuthorAvatarLifecycleState();
        state.Apply(EnabledA);
        Assert.True(state.TryStartResolve(isLoaded: true));

        var enabledB = new AuthorAvatarEffectiveState(
            "b@example.com\nAuthor B",
            true,
            true,
            1);
        var transition = state.Apply(enabledB);

        Assert.True(transition.Changed);
        Assert.True(transition.CancelPending);
        Assert.False(state.IsCurrent(EnabledA.LookupKey));
        Assert.True(state.TryStartResolve(isLoaded: true));
        Assert.False(state.TryStartResolve(isLoaded: true));
    }

    [Theory]
    [InlineData(AuthorAvatarResolutionStatus.ResolvedNoImage)]
    [InlineData(AuthorAvatarResolutionStatus.Faulted)]
    public void CompletedNoImageAndFaultedStatesDoNotRetryOnLifecycle(
        AuthorAvatarResolutionStatus completedState)
    {
        var state = new AuthorAvatarLifecycleState();
        state.Apply(EnabledA);
        Assert.True(state.TryStartResolve(isLoaded: true));

        Assert.True(completedState switch
        {
            AuthorAvatarResolutionStatus.ResolvedNoImage =>
                state.TrySetResolvedNoImage(EnabledA.LookupKey),
            AuthorAvatarResolutionStatus.Faulted =>
                state.TrySetFaulted(EnabledA.LookupKey),
            _ => false
        });

        Assert.False(state.TryStartResolve(isLoaded: false));
        Assert.False(state.Apply(EnabledA).Changed);
        Assert.False(state.TryStartResolve(isLoaded: true));
        Assert.Equal(completedState, state.ResolutionStatus);
    }

    [Fact]
    public void SettingsInvalidationAllowsOneNewResolveAfterReenable()
    {
        var state = new AuthorAvatarLifecycleState();
        state.Apply(EnabledA);
        Assert.True(state.TryStartResolve(isLoaded: true));

        var hidden = EnabledA with { ShowAuthorAvatars = false };
        var disabled = state.Apply(hidden);
        Assert.True(disabled.Changed);
        Assert.True(disabled.CancelPending);
        Assert.False(state.TryStartResolve(isLoaded: true));

        var reenabled = state.Apply(EnabledA);
        Assert.True(reenabled.Changed);
        Assert.True(state.TryStartResolve(isLoaded: true));
        Assert.False(state.TryStartResolve(isLoaded: true));

        var offline = EnabledA with { OnlineAvatarLookupEnabled = false };
        Assert.True(state.Apply(offline).Changed);
        Assert.False(state.TryStartResolve(isLoaded: true));

        Assert.True(state.Apply(EnabledA).Changed);
        Assert.True(state.TryStartResolve(isLoaded: true));
    }

    [Fact]
    public void NormalizedIdentityUsesNormalizedEmailAndTrimmedName()
    {
        Assert.Equal(
            "john@example.com\nJohn Doe",
            AuthorAvatarLifecycleState.NormalizeIdentity(
                " John Doe ",
                " John@Example.com "));
    }
}
