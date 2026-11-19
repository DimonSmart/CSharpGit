namespace CSharpGit.Presentation.Controls;

internal enum CollectionViewportAdjustment
{
    None,
    ResetToTop,
    ClampToContent
}

/// <summary>
/// Distinguishes a new logical list from an ordinary update of the same list.
/// The view adapter applies the adjustment after layout; this policy has no UI dependencies.
/// </summary>
internal sealed class CollectionViewportState
{
    private string? _contextKey;
    private int _itemCount;
    private bool _initialized;

    public CollectionViewportAdjustment Observe(string? contextKey, int itemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);

        var contextChanged = !_initialized
            || !string.Equals(_contextKey, contextKey, StringComparison.Ordinal);
        var previousCount = _itemCount;
        _contextKey = contextKey;
        _itemCount = itemCount;
        _initialized = true;

        if (contextChanged || itemCount == 0 && previousCount > 0)
            return CollectionViewportAdjustment.ResetToTop;

        if (itemCount < previousCount)
            return CollectionViewportAdjustment.ClampToContent;

        return CollectionViewportAdjustment.None;
    }
}
