namespace CSharpGit.Presentation.Controls;

internal enum AuthorAvatarResolutionStatus
{
    NotStarted,
    Pending,
    ResolvedImage,
    ResolvedNoImage,
    Faulted
}

internal readonly record struct AuthorAvatarLookupKey(
    string Identity,
    long ServiceGeneration);

internal readonly record struct AuthorAvatarEffectiveState(
    string Identity,
    bool ShowAuthorAvatars,
    bool OnlineAvatarLookupEnabled,
    long ServiceGeneration)
{
    internal bool LookupEnabled => ShowAuthorAvatars && OnlineAvatarLookupEnabled;
    internal AuthorAvatarLookupKey LookupKey => new(Identity, ServiceGeneration);
}

internal readonly record struct AuthorAvatarStateTransition(
    bool Changed,
    bool CancelPending);

internal sealed class AuthorAvatarLifecycleState
{
    private bool _hasEffectiveState;
    private AuthorAvatarEffectiveState _effectiveState;

    internal bool HasEffectiveState => _hasEffectiveState;

    internal AuthorAvatarEffectiveState EffectiveState =>
        _hasEffectiveState
            ? _effectiveState
            : throw new InvalidOperationException("No effective avatar state has been established.");

    internal AuthorAvatarResolutionStatus ResolutionStatus { get; private set; } =
        AuthorAvatarResolutionStatus.NotStarted;

    internal AuthorAvatarStateTransition Apply(AuthorAvatarEffectiveState next)
    {
        if (_hasEffectiveState && _effectiveState == next)
            return default;

        var cancelPending = ResolutionStatus == AuthorAvatarResolutionStatus.Pending;
        _effectiveState = next;
        _hasEffectiveState = true;
        ResolutionStatus = AuthorAvatarResolutionStatus.NotStarted;
        return new AuthorAvatarStateTransition(Changed: true, CancelPending: cancelPending);
    }

    internal bool TryStartResolve(bool isLoaded)
    {
        if (!_hasEffectiveState
            || !isLoaded
            || !_effectiveState.LookupEnabled
            || ResolutionStatus != AuthorAvatarResolutionStatus.NotStarted)
        {
            return false;
        }

        ResolutionStatus = AuthorAvatarResolutionStatus.Pending;
        return true;
    }

    internal bool IsResolveDeduplicated(bool isLoaded) =>
        _hasEffectiveState
        && isLoaded
        && _effectiveState.LookupEnabled
        && ResolutionStatus != AuthorAvatarResolutionStatus.NotStarted;

    internal bool IsCurrent(AuthorAvatarLookupKey key) =>
        _hasEffectiveState && _effectiveState.LookupKey == key;

    internal bool IsPending(AuthorAvatarLookupKey key) =>
        IsCurrent(key) && ResolutionStatus == AuthorAvatarResolutionStatus.Pending;

    internal bool TrySetResolvedImage(AuthorAvatarLookupKey key) =>
        TryComplete(key, AuthorAvatarResolutionStatus.ResolvedImage);

    internal bool TrySetResolvedNoImage(AuthorAvatarLookupKey key) =>
        TryComplete(key, AuthorAvatarResolutionStatus.ResolvedNoImage);

    internal bool TrySetFaulted(AuthorAvatarLookupKey key) =>
        TryComplete(key, AuthorAvatarResolutionStatus.Faulted);

    internal bool TryMarkImageFailed(AuthorAvatarLookupKey key)
    {
        if (!IsCurrent(key) || ResolutionStatus != AuthorAvatarResolutionStatus.ResolvedImage)
            return false;

        ResolutionStatus = AuthorAvatarResolutionStatus.Faulted;
        return true;
    }

    internal static string NormalizeIdentity(string? authorName, string? authorEmail) =>
        (authorEmail ?? string.Empty).Trim().ToLowerInvariant()
        + "\n"
        + (authorName ?? string.Empty).Trim();

    private bool TryComplete(
        AuthorAvatarLookupKey key,
        AuthorAvatarResolutionStatus completedState)
    {
        if (!IsPending(key))
            return false;

        ResolutionStatus = completedState;
        return true;
    }
}
