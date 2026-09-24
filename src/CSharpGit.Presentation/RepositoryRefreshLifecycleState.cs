namespace CSharpGit.Presentation;

internal enum RepositoryProbeResultDisposition
{
    StaleRevision,
    Unchanged,
    RefreshRequired,
    IgnoredWhileLatched
}

internal sealed class RepositoryRefreshLifecycleState
{
    public bool IsRefreshRequired { get; private set; }

    public long BaselineRevision { get; private set; }

    public bool CanQueueProbe => !IsRefreshRequired;

    public void Reset(long baselineRevision)
    {
        BaselineRevision = baselineRevision;
        IsRefreshRequired = false;
    }

    public bool PublishBaseline(long baselineRevision)
    {
        if (baselineRevision <= BaselineRevision) return false;

        BaselineRevision = baselineRevision;
        IsRefreshRequired = false;
        return true;
    }

    public RepositoryProbeResultDisposition ApplyProbeResult(long probeBaselineRevision, bool changed)
    {
        if (probeBaselineRevision != BaselineRevision)
            return RepositoryProbeResultDisposition.StaleRevision;

        if (IsRefreshRequired)
            return RepositoryProbeResultDisposition.IgnoredWhileLatched;

        if (!changed)
            return RepositoryProbeResultDisposition.Unchanged;

        IsRefreshRequired = true;
        return RepositoryProbeResultDisposition.RefreshRequired;
    }
}
