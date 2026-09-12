using CSharpGit.Domain;

namespace CSharpGit.Git;

internal static class GitWorktreeParser
{
    public static IReadOnlyList<WorktreeInfo> Parse(string output, Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (string.IsNullOrEmpty(output)) return [];

        var result = new List<WorktreeInfo>();
        var currentPath = NormalizePath(repository.WorkingDirectory, repository.WorkingDirectory);
        var record = new WorktreeRecord();

        foreach (var rawField in output.Split('\0'))
        {
            var field = rawField.TrimEnd('\r', '\n');
            if (field.Length == 0)
            {
                AddRecord(result, record, repository.WorkingDirectory, currentPath);
                record = new WorktreeRecord();
                continue;
            }

            if (field.StartsWith("worktree ", StringComparison.Ordinal))
            {
                if (record.Path is not null)
                {
                    AddRecord(result, record, repository.WorkingDirectory, currentPath);
                    record = new WorktreeRecord();
                }
                record.Path = field[9..];
            }
            else if (field.StartsWith("HEAD ", StringComparison.Ordinal))
            {
                record.Head = field[5..];
            }
            else if (field.StartsWith("branch ", StringComparison.Ordinal))
            {
                var branch = field[7..];
                record.Branch = branch.StartsWith("refs/heads/", StringComparison.Ordinal)
                    ? branch[11..]
                    : branch;
            }
            else if (string.Equals(field, "detached", StringComparison.Ordinal))
            {
                record.IsDetached = true;
            }
            else if (string.Equals(field, "locked", StringComparison.Ordinal))
            {
                record.IsLocked = true;
            }
            else if (field.StartsWith("locked ", StringComparison.Ordinal))
            {
                record.IsLocked = true;
                record.LockReason = field[7..];
            }
            else if (string.Equals(field, "prunable", StringComparison.Ordinal)
                     || field.StartsWith("prunable ", StringComparison.Ordinal))
            {
                record.IsPrunable = true;
            }
        }

        AddRecord(result, record, repository.WorkingDirectory, currentPath);
        return result;
    }

    private static void AddRecord(
        ICollection<WorktreeInfo> result,
        WorktreeRecord record,
        string basePath,
        string currentPath)
    {
        if (string.IsNullOrWhiteSpace(record.Path)) return;

        var path = NormalizePath(record.Path, basePath);
        var branch = string.IsNullOrWhiteSpace(record.Branch) ? null : record.Branch;
        result.Add(new WorktreeInfo(
            path,
            record.Head ?? string.Empty,
            branch,
            PathEquals(path, currentPath),
            record.IsDetached || branch is null,
            record.IsLocked,
            string.IsNullOrWhiteSpace(record.LockReason) ? null : record.LockReason,
            record.IsPrunable));
    }

    private static string NormalizePath(string path, string basePath)
    {
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, Path.GetFullPath(basePath));
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class WorktreeRecord
    {
        public string? Path { get; set; }
        public string? Head { get; set; }
        public string? Branch { get; set; }
        public bool IsDetached { get; set; }
        public bool IsLocked { get; set; }
        public string? LockReason { get; set; }
        public bool IsPrunable { get; set; }
    }
}
