using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.ViewModels;

internal sealed class DisplayedRepositoryRefreshBaseline
{
    public RepositoryRefreshFingerprint? Fingerprint { get; private set; }

    public long Revision { get; private set; }

    public bool Publish(RepositoryRefreshFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        var fingerprintChanged = !Equals(Fingerprint, fingerprint);
        Fingerprint = fingerprint;
        Revision++;
        return fingerprintChanged;
    }

    public bool Clear()
    {
        if (Fingerprint is null) return false;

        Fingerprint = null;
        return true;
    }
}
