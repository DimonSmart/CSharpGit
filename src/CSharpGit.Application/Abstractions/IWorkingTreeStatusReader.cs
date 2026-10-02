using CSharpGit.Domain;

namespace CSharpGit.Application.Abstractions;

public sealed record WorkingTreeStatusEntry(
    string Path,
    string SemanticRecord,
    string? OriginalPath = null,
    bool IsSubmodule = false);

public sealed class WorkingTreeStatusSnapshot : IEquatable<WorkingTreeStatusSnapshot>
{
    private readonly WorkingTreeStatusEntry[] _entries;

    public WorkingTreeStatusSnapshot(IEnumerable<WorkingTreeStatusEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries
            .OrderBy(entry => entry.SemanticRecord, StringComparer.Ordinal)
            .ToArray();
    }

    public static WorkingTreeStatusSnapshot Empty { get; } = new([]);

    public IReadOnlyList<WorkingTreeStatusEntry> Entries => _entries;

    public bool Equals(WorkingTreeStatusSnapshot? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || _entries.Length != other._entries.Length) return false;

        for (var index = 0; index < _entries.Length; index++)
        {
            if (!StringComparer.Ordinal.Equals(
                    _entries[index].SemanticRecord,
                    other._entries[index].SemanticRecord))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is WorkingTreeStatusSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in _entries)
            hash.Add(entry.SemanticRecord, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

public interface IWorkingTreeStatusReader
{
    Task<WorkingTreeStatusSnapshot> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default);
}
