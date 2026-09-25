namespace CSharpGit.Presentation.Controls;

internal readonly record struct HistoryRenderDiagnosticSnapshot(
    long GraphControlsCreated,
    long GeometryUpdateAttempts,
    long GeometryRebuilds,
    long AuthorAvatarsCreated,
    long ExplicitAvatarConfigurations,
    long RecursiveGraphLayoutTraversals,
    long RecursiveAvatarConfigurationTraversals);

internal static class HistoryRenderDiagnostics
{
    private static int _enabled =
        string.Equals(
            Environment.GetEnvironmentVariable("CSHARPGIT_HISTORY_RENDER_DIAGNOSTICS"),
            "1",
            StringComparison.Ordinal)
            ? 1
            : 0;

    private static long _graphControlsCreated;
    private static long _geometryUpdateAttempts;
    private static long _geometryRebuilds;
    private static long _authorAvatarsCreated;
    private static long _explicitAvatarConfigurations;
    private static long _recursiveGraphLayoutTraversals;
    private static long _recursiveAvatarConfigurationTraversals;

    private static bool Enabled => Volatile.Read(ref _enabled) != 0;

    internal static void EnableForCheck() => Volatile.Write(ref _enabled, 1);

    internal static void Reset()
    {
        Interlocked.Exchange(ref _graphControlsCreated, 0);
        Interlocked.Exchange(ref _geometryUpdateAttempts, 0);
        Interlocked.Exchange(ref _geometryRebuilds, 0);
        Interlocked.Exchange(ref _authorAvatarsCreated, 0);
        Interlocked.Exchange(ref _explicitAvatarConfigurations, 0);
        Interlocked.Exchange(ref _recursiveGraphLayoutTraversals, 0);
        Interlocked.Exchange(ref _recursiveAvatarConfigurationTraversals, 0);
    }

    internal static HistoryRenderDiagnosticSnapshot Snapshot() => new(
        Volatile.Read(ref _graphControlsCreated),
        Volatile.Read(ref _geometryUpdateAttempts),
        Volatile.Read(ref _geometryRebuilds),
        Volatile.Read(ref _authorAvatarsCreated),
        Volatile.Read(ref _explicitAvatarConfigurations),
        Volatile.Read(ref _recursiveGraphLayoutTraversals),
        Volatile.Read(ref _recursiveAvatarConfigurationTraversals));

    internal static void GraphControlCreated()
    {
        if (Enabled) Interlocked.Increment(ref _graphControlsCreated);
    }

    internal static void GeometryUpdateAttempted()
    {
        if (Enabled) Interlocked.Increment(ref _geometryUpdateAttempts);
    }

    internal static void GeometryRebuilt()
    {
        if (Enabled) Interlocked.Increment(ref _geometryRebuilds);
    }

    internal static void AuthorAvatarCreated()
    {
        if (Enabled) Interlocked.Increment(ref _authorAvatarsCreated);
    }

    internal static void ExplicitAvatarConfigured()
    {
        if (Enabled) Interlocked.Increment(ref _explicitAvatarConfigurations);
    }

    internal static void RecursiveGraphLayoutTraversal()
    {
        if (Enabled) Interlocked.Increment(ref _recursiveGraphLayoutTraversals);
    }

    internal static void RecursiveAvatarConfigurationTraversal()
    {
        if (Enabled) Interlocked.Increment(ref _recursiveAvatarConfigurationTraversals);
    }
}
