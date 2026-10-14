namespace CSharpGit.Presentation;

internal enum RepositoryStatusCheckDisposition
{
    StaleRevision,
    UpToDate,
    RefreshRequired,
    IgnoredWhileLatched
}

internal sealed class RepositoryRefreshLifecycleState
{
    public bool IsRefreshRequired { get; private set; }

    public long BaselineRevision { get; private set; }

    public bool CanRunBackgroundStatusCheck => !IsRefreshRequired;

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

    public RepositoryStatusCheckDisposition ApplyStatusCheckResult(
        long baselineRevision,
        bool changed)
    {
        if (baselineRevision != BaselineRevision)
            return RepositoryStatusCheckDisposition.StaleRevision;

        if (IsRefreshRequired)
            return RepositoryStatusCheckDisposition.IgnoredWhileLatched;

        if (!changed)
            return RepositoryStatusCheckDisposition.UpToDate;

        IsRefreshRequired = true;
        return RepositoryStatusCheckDisposition.RefreshRequired;
    }

    public RepositoryStatusCheckDisposition Latch(long baselineRevision) =>
        ApplyStatusCheckResult(baselineRevision, changed: true);
}
