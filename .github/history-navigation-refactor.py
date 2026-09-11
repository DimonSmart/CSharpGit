from pathlib import Path
import re


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, content):
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content.replace("\r\n", "\n"), encoding="utf-8", newline="\n")


def replace_once(path, old, new):
    text = read(path)
    if text.count(old) != 1:
        raise RuntimeError(f"Expected exactly one occurrence in {path}: {old[:100]!r}; found {text.count(old)}")
    write(path, text.replace(old, new, 1))


def replace_between(path, start, end, replacement):
    text = read(path)
    start_index = text.find(start)
    if start_index < 0:
        raise RuntimeError(f"Start marker not found in {path}: {start!r}")
    end_index = text.find(end, start_index + len(start))
    if end_index < 0:
        raise RuntimeError(f"End marker not found in {path}: {end!r}")
    write(path, text[:start_index] + replacement + text[end_index:])


# Application API: keep legacy detail APIs for non-navigation callers, but expose strict lazy APIs.
replace_once(
    "src/CSharpGit.Application/Abstractions/IHistoryService.cs",
    "    Task<CommitDetails> ReadCommitAsync(Repository repository, string hash, CancellationToken cancellationToken = default);\n    Task<FileDiff> ReadDiffAsync(Repository repository, string hash, string path, CancellationToken cancellationToken = default);",
    "    Task<CommitDetails> ReadCommitAsync(Repository repository, string hash, CancellationToken cancellationToken = default);\n    Task<IReadOnlyList<ChangedFile>> ReadChangedFilesAsync(\n        Repository repository,\n        string commitHash,\n        string? parentHash,\n        CancellationToken cancellationToken = default);\n    Task<FileDiff> ReadDiffAsync(Repository repository, string hash, string path, CancellationToken cancellationToken = default);\n    Task<FileDiff> ReadDiffAsync(\n        Repository repository,\n        string commitHash,\n        string? parentHash,\n        ChangedFile file,\n        CancellationToken cancellationToken = default);"
)

# The reference-history implementation is a history source used by GitFileAwareHistoryService;
# it no longer needs to implement the full lazy-details interface itself.
replace_once(
    "src/CSharpGit.Git/GitReferenceHistoryService.cs",
    "public sealed class GitReferenceHistoryService : IReferenceHistoryService, IHistoryService",
    "public sealed class GitReferenceHistoryService : IReferenceHistoryService"
)

write("src/CSharpGit.Git/GitProcessRunner.cs", r'''using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CSharpGit.Git;

internal sealed class GitProcessRunner
{
    private readonly string _gitExecutable;
    private int _invocationCount;

    public GitProcessRunner(string gitExecutable)
    {
        _gitExecutable = string.IsNullOrWhiteSpace(gitExecutable) ? "git" : gitExecutable;
    }

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public async Task<string> RunAsync(
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        Interlocked.Increment(ref _invocationCount);
        return await RunProcessAsync(
            _gitExecutable,
            workingDirectory,
            operation,
            cancellationToken,
            arguments);
    }

    internal static async Task<string> RunProcessAsync(
        string executable,
        string workingDirectory,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments,
        Action<int>? processStarted = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();
        processStarted?.Invoke(process.Id);

        // Do not cancel pipe readers independently. On cancellation the process tree is
        // terminated first, then both streams are drained before the Process is disposed.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await WaitForExitAfterKillAsync(process);
            await DrainAfterKillAsync(outputTask, errorTask);
            throw;
        }

        var output = await outputTask;
        var error = (await errorTask).Trim();
        cancellationToken.ThrowIfCancellationRequested();

        stopwatch.Stop();
        Trace.WriteLine($"Git command operation={operation} duration={stopwatch.ElapsedMilliseconds}ms");
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"Git exited with code {process.ExitCode}."
                : $"Git exited with code {process.ExitCode}: {error}");
        return output.TrimEnd('\r', '\n');
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process can win the race and exit between HasExited and Kill.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Already exited/disposed by the time the cancellation continuation ran.
        }
    }

    private static async Task DrainAfterKillAsync(Task<string> outputTask, Task<string> errorTask)
    {
        try
        {
            await Task.WhenAll(outputTask, errorTask);
        }
        catch (IOException)
        {
            // A killed process can close a redirected pipe while the async read completes.
        }
    }
}
''')

write("src/CSharpGit.Git/GitFileAwareHistoryService.cs", r'''using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitFileAwareHistoryService : IHistoryService, IReferenceHistoryService
{
    private readonly GitReferenceHistoryService _history;
    private readonly GitProcessRunner _runner;

    public GitFileAwareHistoryService(
        GitReferenceHistoryService history,
        IRepositoryFileVersionService versions)
        : this(history, versions, new GitCliOptions())
    {
    }

    public GitFileAwareHistoryService(
        GitReferenceHistoryService history,
        IRepositoryFileVersionService versions,
        GitCliOptions options)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(options);
        _runner = new GitProcessRunner(string.IsNullOrWhiteSpace(options.ExecutablePath) ? "git" : options.ExecutablePath);
    }

    internal int GitInvocationCount => _runner.InvocationCount;

    public Task<HistoryPage> ReadHistoryAsync(
        Repository repository,
        HistoryQuery query,
        CancellationToken cancellationToken = default) =>
        _history.ReadHistoryAsync(repository, query, cancellationToken);

    public Task<HistoryPage> ReadHistoryThroughCommitAsync(
        Repository repository,
        HistoryScope scope,
        string targetHash,
        int trailingCount = 100,
        CancellationToken cancellationToken = default) =>
        _history.ReadHistoryThroughCommitAsync(repository, scope, targetHash, trailingCount, cancellationToken);

    public Task<HistoryPage> ReadHistoryAsync(
        Repository repository,
        string reference,
        string? filter,
        int skip,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        _history.ReadHistoryAsync(repository, reference, filter, skip, take, cancellationToken);

    // Retained for explicit/non-navigation callers. History navigation itself does not call it.
    public async Task<CommitDetails> ReadCommitAsync(
        Repository repository,
        string hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(hash);
        var metadata = await _history.ReadCommitAsync(repository, hash, cancellationToken);
        var parentHash = metadata.Commit.Parents.FirstOrDefault();
        var files = await ReadChangedFilesAsync(repository, hash, parentHash, cancellationToken);
        return new CommitDetails(metadata.Commit, files);
    }

    public async Task<IReadOnlyList<ChangedFile>> ReadChangedFilesAsync(
        Repository repository,
        string commitHash,
        string? parentHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(commitHash);
        if (parentHash is not null) ValidateObjectName(parentHash);

        string output;
        if (parentHash is null)
        {
            output = await _runner.RunAsync(
                repository.WorkingDirectory,
                "ChangedFiles",
                cancellationToken,
                "diff-tree", "--root", "--no-commit-id", "--raw", "--numstat", "-r", "-z",
                "--find-renames", "--find-copies", commitHash);
        }
        else
        {
            output = await _runner.RunAsync(
                repository.WorkingDirectory,
                "ChangedFiles",
                cancellationToken,
                "diff", "--raw", "--numstat", "-z", "--no-ext-diff",
                "--find-renames", "--find-copies", parentHash, commitHash);
        }

        var stats = ParseNumStat(output).ToList();
        var files = new List<ChangedFile>();
        foreach (var entry in ParseRawDiff(output))
        {
            var stat = stats.FirstOrDefault(candidate =>
                string.Equals(candidate.NewPath, entry.NewPath, StringComparison.Ordinal) &&
                string.Equals(candidate.OldPath, entry.OldPath, StringComparison.Ordinal));
            stat ??= stats.FirstOrDefault(candidate => string.Equals(candidate.NewPath, entry.NewPath, StringComparison.Ordinal));

            files.Add(new ChangedFile(
                entry.NewPath,
                stat?.AddedLines,
                stat?.RemovedLines,
                stat?.IsBinary ?? false,
                entry.Status is 'R' or 'C' ? entry.OldPath : null,
                entry.Status.ToString()));
        }
        return files;
    }

    // Legacy overload retained for non-navigation callers. It resolves metadata once and then
    // delegates to the same first-parent lazy operations used by the UI.
    public async Task<FileDiff> ReadDiffAsync(
        Repository repository,
        string hash,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ValidateObjectName(hash);
        ValidateGitPath(path);
        var metadata = await _history.ReadCommitAsync(repository, hash, cancellationToken);
        var parentHash = metadata.Commit.Parents.FirstOrDefault();
        var files = await ReadChangedFilesAsync(repository, hash, parentHash, cancellationToken);
        var file = files.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, path, StringComparison.Ordinal) ||
            string.Equals(candidate.OriginalPath, path, StringComparison.Ordinal));
        if (file is null) throw new InvalidOperationException("The selected file is no longer part of this commit diff.");
        return await ReadDiffAsync(repository, hash, parentHash, file, cancellationToken);
    }

    public async Task<FileDiff> ReadDiffAsync(
        Repository repository,
        string commitHash,
        string? parentHash,
        ChangedFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(file);
        ValidateObjectName(commitHash);
        if (parentHash is not null) ValidateObjectName(parentHash);
        ValidateGitPath(file.Path);
        if (file.OriginalPath is not null) ValidateGitPath(file.OriginalPath);

        var paths = file.OriginalPath is { } originalPath &&
                    !string.Equals(originalPath, file.Path, StringComparison.Ordinal)
            ? new[] { originalPath, file.Path }
            : new[] { file.Path };

        var arguments = new List<string>();
        if (parentHash is null)
        {
            arguments.AddRange(["show", "--format=", "--root", "--no-ext-diff", "--find-renames", "--find-copies", commitHash, "--"]);
        }
        else
        {
            arguments.AddRange(["diff", "--no-ext-diff", "--find-renames", "--find-copies", parentHash, commitHash, "--"]);
        }
        arguments.AddRange(paths);

        var output = await _runner.RunAsync(
            repository.WorkingDirectory,
            "Diff",
            cancellationToken,
            arguments.ToArray());
        var binary = output.Contains("Binary files ", StringComparison.Ordinal) ||
                     output.Contains("GIT binary patch", StringComparison.Ordinal);
        return new FileDiff(file.Path, binary, binary ? [] : ParseDiffLines(output));
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadFileStatusesAsync(
        Repository repository,
        string commitHash,
        CancellationToken cancellationToken = default)
    {
        var details = await ReadCommitAsync(repository, commitHash, cancellationToken);
        return details.Files.ToDictionary(file => file.Path, file => file.Status, StringComparer.Ordinal);
    }

    private static IEnumerable<RawDiffEntry> ParseRawDiff(string output)
    {
        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < records.Length; index++)
        {
            var metadataText = records[index].TrimStart('\r', '\n');
            if (!metadataText.StartsWith(':')) continue;
            var metadata = metadataText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length < 5 || index + 1 >= records.Length) continue;
            var statusText = metadata[4];
            if (statusText.Length == 0) continue;

            var status = statusText[0];
            var firstPath = records[++index];
            var oldPath = firstPath;
            var newPath = firstPath;
            if (status is 'R' or 'C' && index + 1 < records.Length)
                newPath = records[++index];
            yield return new RawDiffEntry(status, oldPath, newPath);
        }
    }

    private static IEnumerable<NumStatEntry> ParseNumStat(string output)
    {
        var records = output.Split('\0');
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index].TrimStart('\r', '\n');
            if (record.Length == 0) continue;
            var fields = record.Split('\t', 3);
            if (fields.Length != 3) continue;

            var binary = fields[0] == "-" || fields[1] == "-";
            int? added = int.TryParse(fields[0], out var addedValue) ? addedValue : null;
            int? removed = int.TryParse(fields[1], out var removedValue) ? removedValue : null;
            if (fields[2].Length > 0)
            {
                yield return new NumStatEntry(fields[2], fields[2], added, removed, binary);
                continue;
            }

            if (index + 2 >= records.Length) continue;
            var oldPath = records[++index];
            var newPath = records[++index];
            yield return new NumStatEntry(oldPath, newPath, added, removed, binary);
        }
    }

    private static IReadOnlyList<DiffLine> ParseDiffLines(string output) => output.Split('\n')
        .Select(line => new DiffLine(
            line.TrimEnd('\r'),
            line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("@@") || line.StartsWith("diff ") || line.StartsWith("index ")
                ? DiffLineKind.Header
                : line.StartsWith('+')
                    ? DiffLineKind.Added
                    : line.StartsWith('-')
                        ? DiffLineKind.Removed
                        : DiffLineKind.Context))
        .ToList();

    private static void ValidateObjectName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Invalid Git object id.", nameof(value));
    }

    private static void ValidateGitPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') || Path.IsPathRooted(path))
            throw new ArgumentException("Invalid Git file path.", nameof(path));
    }

    private sealed record RawDiffEntry(char Status, string OldPath, string NewPath);
    private sealed record NumStatEntry(string OldPath, string NewPath, int? AddedLines, int? RemovedLines, bool IsBinary);
}
''')

# Core VM: selection is now a cheap state transition; asynchronous commit-change work lives in a focused partial.
vm = "src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.cs"
text = read(vm)
text = text.replace("    private long _commitLoadGeneration;\n", "")
text = text.replace("    private CommitDetails? _selectedCommit;\n", "")
text = text.replace("    private bool _isCommitLoading;\n", "")
old_props = "    public HistoryRow? SelectedHistoryRow { get => _selectedHistoryRow; set { if (ReferenceEquals(_selectedHistoryRow, value)) return; _selectedHistoryRow = value; Notify(); Notify(nameof(DetailsVisibility)); _ = LoadCommitAsync(); } }\n    public CommitDetails? SelectedCommit { get => _selectedCommit; private set { _selectedCommit = value; Notify(); } }\n    public ChangedFile? SelectedFile { get => _selectedFile; set { if (_selectedFile == value) return; _selectedFile = value; Notify(); _ = LoadDiffAsync(); } }\n    public FileDiff? SelectedDiff { get => _selectedDiff; private set { _selectedDiff = value; Notify(); Notify(nameof(DiffVisibility)); Notify(nameof(BinaryVisibility)); } }\n    public bool IsCommitLoading { get => _isCommitLoading; private set { if (_isCommitLoading == value) return; _isCommitLoading = value; Notify(); Notify(nameof(CommitLoadingVisibility)); } }\n    public bool IsDiffLoading { get => _isDiffLoading; private set { if (_isDiffLoading == value) return; _isDiffLoading = value; Notify(); Notify(nameof(DiffLoadingVisibility)); } }"
new_props = "    public HistoryRow? SelectedHistoryRow { get => _selectedHistoryRow; set { if (ReferenceEquals(_selectedHistoryRow, value)) return; _selectedHistoryRow = value; Notify(); Notify(nameof(DetailsVisibility)); OnSelectedHistoryRowChanged(); } }\n    public ChangedFile? SelectedFile { get => _selectedFile; set { if (_selectedFile == value) return; _selectedFile = value; Notify(); OnSelectedFileChanged(); } }\n    public FileDiff? SelectedDiff { get => _selectedDiff; private set { _selectedDiff = value; Notify(); Notify(nameof(DiffVisibility)); Notify(nameof(BinaryVisibility)); } }\n    public bool IsDiffLoading { get => _isDiffLoading; private set { if (_isDiffLoading == value) return; _isDiffLoading = value; Notify(); Notify(nameof(DiffLoadingVisibility)); } }"
if text.count(old_props) != 1:
    raise RuntimeError("OpenRepositoryViewModel selection property block changed")
text = text.replace(old_props, new_props, 1)
text = text.replace("    public Visibility CommitLoadingVisibility => IsCommitLoading ? Visibility.Visible : Visibility.Collapsed;\n", "")
old_repo = "    public Repository? Repository { get => _repository; private set { _repository = value; Notify(); Notify(nameof(RepositoryVisibility)); Notify(nameof(PickerVisibility)); Notify(nameof(RepositoryKind)); Notify(nameof(CanForcePushWithLease)); ((AsyncCommand)RefreshHistoryCommand).RaiseCanExecuteChanged(); } }"
new_repo = "    public Repository? Repository { get => _repository; private set { if (ReferenceEquals(_repository, value)) return; ResetCommitChangesSession(); _repository = value; Notify(); Notify(nameof(RepositoryVisibility)); Notify(nameof(PickerVisibility)); Notify(nameof(RepositoryKind)); Notify(nameof(CanForcePushWithLease)); ((AsyncCommand)RefreshHistoryCommand).RaiseCanExecuteChanged(); } }"
if text.count(old_repo) != 1:
    raise RuntimeError("Repository property changed")
text = text.replace(old_repo, new_repo, 1)
text = text.replace("                    SelectedCommit = null;\n", "")
start = text.find("    private async Task LoadCommitAsync()")
end = text.find("    private void Notify(", start)
if start < 0 or end < 0:
    raise RuntimeError("Old commit/diff load methods not found")
text = text[:start] + text[end:]
write(vm, text)

write("src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.CommitChanges.cs", r'''using System.Diagnostics;
using CSharpGit.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation.ViewModels;

public sealed partial class OpenRepositoryViewModel
{
    private const int ChangedFilesDebounceMilliseconds = 120;
    private const int ChangedFilesCacheEntries = 64;
    private const int DiffCacheEntries = 96;
    private const int DiffCacheCharacters = 4 * 1024 * 1024;

    private CancellationTokenSource? _changedFilesLoadCts;
    private CancellationTokenSource? _diffLoadCts;
    private long _changedFilesLoadGeneration;
    private IReadOnlyList<ChangedFile> _selectedChangedFiles = [];
    private bool _isChangedFilesLoading;
    private bool _isChangesViewActive;
    private readonly BoundedLruCache<ChangedFilesCacheKey, IReadOnlyList<ChangedFile>> _changedFilesCache =
        new(ChangedFilesCacheEntries, ChangedFilesCacheEntries, _ => 1);
    private readonly BoundedLruCache<DiffCacheKey, FileDiff> _diffCache =
        new(DiffCacheEntries, DiffCacheCharacters, EstimateDiffSize);

    public IReadOnlyList<ChangedFile> SelectedChangedFiles
    {
        get => _selectedChangedFiles;
        private set
        {
            if (ReferenceEquals(_selectedChangedFiles, value)) return;
            _selectedChangedFiles = value;
            Notify();
        }
    }

    public bool IsChangedFilesLoading
    {
        get => _isChangedFilesLoading;
        private set
        {
            if (_isChangedFilesLoading == value) return;
            _isChangedFilesLoading = value;
            Notify();
            Notify(nameof(ChangedFilesLoadingVisibility));
        }
    }

    public Visibility ChangedFilesLoadingVisibility => IsChangedFilesLoading ? Visibility.Visible : Visibility.Collapsed;
    public bool IsChangesViewActive => _isChangesViewActive;

    public void SetChangesViewActive(bool active)
    {
        if (_isChangesViewActive == active) return;
        _isChangesViewActive = active;
        Notify(nameof(IsChangesViewActive));

        if (!active)
        {
            InvalidateChangedFilesLoad();
            InvalidateDiffLoad();
            IsChangedFilesLoading = false;
            IsDiffLoading = false;
            return;
        }

        _ = EnsureChangedFilesLoadedAsync(debounce: false);
    }

    private void OnSelectedHistoryRowChanged()
    {
        InvalidateChangedFilesLoad();
        InvalidateDiffLoad();
        SelectedChangedFiles = [];
        SelectedFile = null;
        SelectedDiff = null;
        IsChangedFilesLoading = false;
        IsDiffLoading = false;

        if (_isChangesViewActive)
            _ = EnsureChangedFilesLoadedAsync(debounce: true);
    }

    private void OnSelectedFileChanged()
    {
        InvalidateDiffLoad();
        SelectedDiff = null;
        IsDiffLoading = false;
        if (_isChangesViewActive && SelectedFile is not null)
            _ = LoadSelectedDiffAsync();
    }

    private void ResetCommitChangesSession()
    {
        InvalidateChangedFilesLoad();
        InvalidateDiffLoad();
        _changedFilesCache.Clear();
        _diffCache.Clear();
        SelectedChangedFiles = [];
        SelectedFile = null;
        SelectedDiff = null;
        IsChangedFilesLoading = false;
        IsDiffLoading = false;
    }

    private async Task EnsureChangedFilesLoadedAsync(bool debounce)
    {
        var repository = Repository;
        var row = SelectedHistoryRow;
        if (!_isChangesViewActive || repository is null || row is null) return;

        var parentHash = row.Commit.Parents.FirstOrDefault();
        var cacheKey = new ChangedFilesCacheKey(repository.GitDirectory, row.Commit.Hash, parentHash);
        if (_changedFilesCache.TryGet(cacheKey, out var cached))
        {
            _logger.LogDebug("LoadChangedFiles commit={Commit} duration={Duration}ms cache={Cache}", row.Commit.Hash, 0, "hit");
            PublishChangedFiles(repository, row, cached);
            return;
        }

        var generation = Interlocked.Increment(ref _changedFilesLoadGeneration);
        var cancellation = new CancellationTokenSource();
        ReplaceCancellation(ref _changedFilesLoadCts, cancellation);
        IsChangedFilesLoading = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (debounce)
                await Task.Delay(ChangedFilesDebounceMilliseconds, cancellation.Token);

            var files = (await _historyService.ReadChangedFilesAsync(
                repository,
                row.Commit.Hash,
                parentHash,
                cancellation.Token)).ToArray();
            stopwatch.Stop();

            if (!IsCurrentChangedFilesRequest(generation, cancellation, repository, row)) return;
            _changedFilesCache.Set(cacheKey, files);
            _logger.LogDebug("LoadChangedFiles commit={Commit} duration={Duration}ms cache={Cache}", row.Commit.Hash, stopwatch.ElapsedMilliseconds, "miss");
            PublishChangedFiles(repository, row, files);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (IsCurrentChangedFilesRequest(generation, cancellation, repository, row))
            {
                SelectedChangedFiles = [];
                SelectedFile = null;
                SelectedDiff = null;
                ErrorMessage = $"Could not read changes: {exception.Message}";
                _logger.LogWarning(exception, "Changed-files loading failed for {Commit}", row.Commit.Hash);
            }
        }
        finally
        {
            stopwatch.Stop();
            if (IsCurrentChangedFilesRequest(generation, cancellation, repository, row))
                IsChangedFilesLoading = false;
        }
    }

    private void PublishChangedFiles(Repository repository, HistoryRow row, IReadOnlyList<ChangedFile> files)
    {
        if (!_isChangesViewActive || !ReferenceEquals(repository, Repository) || !ReferenceEquals(row, SelectedHistoryRow)) return;
        SelectedChangedFiles = files;
        var first = files.FirstOrDefault();
        if (!ReferenceEquals(SelectedFile, first)) SelectedFile = first;
        else if (first is not null) _ = LoadSelectedDiffAsync();
    }

    private async Task LoadSelectedDiffAsync()
    {
        var repository = Repository;
        var row = SelectedHistoryRow;
        var file = SelectedFile;
        if (!_isChangesViewActive || repository is null || row is null || file is null) return;

        var parentHash = row.Commit.Parents.FirstOrDefault();
        var cacheKey = new DiffCacheKey(repository.GitDirectory, row.Commit.Hash, parentHash, file.Path, file.OriginalPath);
        if (_diffCache.TryGet(cacheKey, out var cached))
        {
            _logger.LogDebug("LoadDiff commit={Commit} path={Path} duration={Duration}ms cache={Cache}", row.Commit.Hash, file.Path, 0, "hit");
            if (_isChangesViewActive && ReferenceEquals(repository, Repository) && ReferenceEquals(row, SelectedHistoryRow) && ReferenceEquals(file, SelectedFile))
                SelectedDiff = cached;
            return;
        }

        var generation = Interlocked.Increment(ref _diffLoadGeneration);
        var cancellation = new CancellationTokenSource();
        ReplaceCancellation(ref _diffLoadCts, cancellation);
        SelectedDiff = null;
        IsDiffLoading = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var diff = await _historyService.ReadDiffAsync(
                repository,
                row.Commit.Hash,
                parentHash,
                file,
                cancellation.Token);
            stopwatch.Stop();

            if (!IsCurrentDiffRequest(generation, cancellation, repository, row, file)) return;
            _diffCache.Set(cacheKey, diff);
            _logger.LogDebug("LoadDiff commit={Commit} path={Path} duration={Duration}ms cache={Cache}", row.Commit.Hash, file.Path, stopwatch.ElapsedMilliseconds, "miss");
            SelectedDiff = diff;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (IsCurrentDiffRequest(generation, cancellation, repository, row, file))
            {
                SelectedDiff = null;
                ErrorMessage = $"Could not read change: {exception.Message}";
                _logger.LogWarning(exception, "Diff loading failed for {Commit} {Path}", row.Commit.Hash, file.Path);
            }
        }
        finally
        {
            stopwatch.Stop();
            if (IsCurrentDiffRequest(generation, cancellation, repository, row, file))
                IsDiffLoading = false;
        }
    }

    private bool IsCurrentChangedFilesRequest(
        long generation,
        CancellationTokenSource cancellation,
        Repository repository,
        HistoryRow row) =>
        !cancellation.IsCancellationRequested &&
        generation == Volatile.Read(ref _changedFilesLoadGeneration) &&
        _isChangesViewActive &&
        ReferenceEquals(repository, Repository) &&
        ReferenceEquals(row, SelectedHistoryRow);

    private bool IsCurrentDiffRequest(
        long generation,
        CancellationTokenSource cancellation,
        Repository repository,
        HistoryRow row,
        ChangedFile file) =>
        !cancellation.IsCancellationRequested &&
        generation == Volatile.Read(ref _diffLoadGeneration) &&
        _isChangesViewActive &&
        ReferenceEquals(repository, Repository) &&
        ReferenceEquals(row, SelectedHistoryRow) &&
        ReferenceEquals(file, SelectedFile);

    private void InvalidateChangedFilesLoad()
    {
        Interlocked.Increment(ref _changedFilesLoadGeneration);
        CancelAndDispose(ref _changedFilesLoadCts);
    }

    private void InvalidateDiffLoad()
    {
        Interlocked.Increment(ref _diffLoadGeneration);
        CancelAndDispose(ref _diffLoadCts);
    }

    private static void ReplaceCancellation(ref CancellationTokenSource? field, CancellationTokenSource replacement)
    {
        var previous = Interlocked.Exchange(ref field, replacement);
        if (previous is null) return;
        previous.Cancel();
        previous.Dispose();
    }

    private static void CancelAndDispose(ref CancellationTokenSource? field)
    {
        var cancellation = Interlocked.Exchange(ref field, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private static int EstimateDiffSize(FileDiff diff)
    {
        long size = diff.Path.Length;
        foreach (var line in diff.Lines) size += line.Text.Length + 1L;
        return (int)Math.Min(size, int.MaxValue);
    }

    private readonly record struct ChangedFilesCacheKey(string Repository, string Commit, string? Parent);
    private readonly record struct DiffCacheKey(string Repository, string Commit, string? Parent, string Path, string? OriginalPath);

    private sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _maxEntries;
        private readonly int _maxCost;
        private readonly Func<TValue, int> _cost;
        private readonly Dictionary<TKey, LinkedListNode<Entry>> _index = [];
        private readonly LinkedList<Entry> _lru = [];
        private int _currentCost;

        public BoundedLruCache(int maxEntries, int maxCost, Func<TValue, int> cost)
        {
            _maxEntries = maxEntries;
            _maxCost = maxCost;
            _cost = cost;
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (!_index.TryGetValue(key, out var node))
            {
                value = default!;
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        public void Set(TKey key, TValue value)
        {
            var cost = Math.Max(1, _cost(value));
            if (cost > _maxCost) return;
            if (_index.Remove(key, out var existing))
            {
                _lru.Remove(existing);
                _currentCost -= existing.Value.Cost;
            }

            var node = new LinkedListNode<Entry>(new Entry(key, value, cost));
            _lru.AddFirst(node);
            _index[key] = node;
            _currentCost += cost;

            while (_index.Count > _maxEntries || _currentCost > _maxCost)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _index.Remove(last.Value.Key);
                _currentCost -= last.Value.Cost;
            }
        }

        public void Clear()
        {
            _index.Clear();
            _lru.Clear();
            _currentCost = 0;
        }

        private sealed record Entry(TKey Key, TValue Value, int Cost);
    }
}
''')

# XAML: commit metadata is already in HistoryRow; tab selection drives lazy Changes activity.
xaml = "src/CSharpGit.Presentation/MainPage.xaml"
text = read(xaml)
text = text.replace('                   HeaderTemplate="{StaticResource PivotHeaderTemplate}"\n                   Visibility="{Binding DetailsVisibility}">',
                    '                   HeaderTemplate="{StaticResource PivotHeaderTemplate}"\n                   SelectionChanged="DetailsTabs_SelectionChanged"\n                   Visibility="{Binding DetailsVisibility}">')
text = text.replace("SelectedCommit.Commit.", "SelectedHistoryRow.Commit.")
write(xaml, text)

write("src/CSharpGit.Presentation/MainPage.Changes.cs", r'''using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly ObservableCollection<ChangedFileTreeNode> _changedFileTreeRoots = [];
    private readonly ObservableCollection<CompactDiffLine> _compactDiffLines = [];
    private bool _changesSurfaceInitialized;
    private bool _changedFileTreeRefreshQueued;

    private void ChangesSurface_Loaded(object sender, RoutedEventArgs args)
    {
        if (_changesSurfaceInitialized) return;
        _changesSurfaceInitialized = true;

        ChangedFilesTree.ItemsSource = _changedFileTreeRoots;
        CompactDiffList.ItemsSource = _compactDiffLines;
        _commitFiles.CollectionChanged += CommitFiles_CollectionChanged;
        _viewModel.PropertyChanged += ChangesViewModel_PropertyChanged;

        RebuildChangedFileTree();
        RebuildCompactDiff();
        UpdateChangesViewActivity();
    }

    private void DetailsTabs_SelectionChanged(object sender, SelectionChangedEventArgs args) =>
        UpdateChangesViewActivity();

    private void UpdateChangesViewActivity() =>
        _viewModel.SetChangesViewActive(ReferenceEquals(DetailsTabs.SelectedItem, FilesTab));

    private void CommitFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!_changesSurfaceInitialized || _changedFileTreeRefreshQueued) return;
        _changedFileTreeRefreshQueued = true;
        if (DispatcherQueue.TryEnqueue(() =>
        {
            _changedFileTreeRefreshQueued = false;
            RebuildChangedFileTree();
        })) return;

        _changedFileTreeRefreshQueued = false;
        RebuildChangedFileTree();
    }

    private void ChangesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedDiff))
        {
            RebuildCompactDiff();
        }
        else if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedFile))
        {
            SyncChangedFileTreeSelection();
        }
        else if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedChangedFiles))
        {
            EnsureCurrentCommitFileSelection();
        }
    }

    private void RebuildChangedFileTree()
    {
        if (!_changesSurfaceInitialized) return;

        var entries = _commitFiles.Select(row => new ChangedFileTreeEntry(row.Status, row.File));
        var roots = ChangedFileTreeNode.Build(entries);

        _changedFileTreeRoots.Clear();
        foreach (var root in roots) _changedFileTreeRoots.Add(root);

        FilesTab.Header = _commitFiles.Count == 0 ? "Changes" : $"Changes ({_commitFiles.Count})";
        EnsureCurrentCommitFileSelection();
        SyncChangedFileTreeSelection();
    }

    private void RebuildCompactDiff()
    {
        if (!_changesSurfaceInitialized) return;

        _compactDiffLines.Clear();
        if (_viewModel.SelectedDiff is not { IsBinary: false } diff) return;
        foreach (var line in CompactDiffLine.Build(diff.Lines)) _compactDiffLines.Add(line);
    }

    private void EnsureCurrentCommitFileSelection()
    {
        if (!_viewModel.IsChangesViewActive) return;
        var files = _viewModel.SelectedChangedFiles;
        if (files.Count == 0) return;

        var selected = _viewModel.SelectedFile;
        var current = selected is null
            ? files[0]
            : files.FirstOrDefault(file => string.Equals(file.Path, selected.Path, StringComparison.Ordinal)) ?? files[0];

        if (ReferenceEquals(selected, current)) return;
        _viewModel.SelectedFile = current;
    }

    private void SyncChangedFileTreeSelection()
    {
        if (!_changesSurfaceInitialized || _viewModel.SelectedFile is null) return;
        var node = FindChangedFileNode(_changedFileTreeRoots, _viewModel.SelectedFile.Path);
        if (node is not null && !ReferenceEquals(ChangedFilesTree.SelectedItem, node))
            ChangedFilesTree.SelectedItem = node;
    }

    private void ChangedFilesTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var node = ResolveChangedFileNode(args.InvokedItem);
        if (node?.Entry is null) return;

        if (!ReferenceEquals(_viewModel.SelectedFile, node.Entry.File) && _viewModel.SelectedFile == node.Entry.File)
            _viewModel.SelectedFile = null;
        _viewModel.SelectedFile = node.Entry.File;
    }

    private static ChangedFileTreeNode? ResolveChangedFileNode(object? value) => value switch
    {
        ChangedFileTreeNode node => node,
        TreeViewNode { Content: ChangedFileTreeNode node } => node,
        TreeViewItem { DataContext: ChangedFileTreeNode node } => node,
        FrameworkElement { DataContext: ChangedFileTreeNode node } => node,
        _ => null
    };

    private static ChangedFileTreeNode? FindChangedFileNode(
        IEnumerable<ChangedFileTreeNode> nodes,
        string path)
    {
        foreach (var node in nodes)
        {
            if (node.Entry is not null && string.Equals(node.Entry.File.Path, path, StringComparison.Ordinal))
                return node;
            if (FindChangedFileNode(node.Children, path) is { } match) return match;
        }
        return null;
    }
}
''')

# MainPage projection no longer performs a second Git status query.
page = "src/CSharpGit.Presentation/MainPage.xaml.cs"
text = read(page)
text = text.replace("        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.SelectedCommit))\n        {\n            _ = RefreshCommitFilesAsync();\n        }",
                    "        else if (eventArgs.PropertyName == nameof(OpenRepositoryViewModel.SelectedChangedFiles))\n        {\n            _ = RefreshCommitFilesAsync();\n        }")
start = text.find("    private async Task RefreshCommitFilesAsync()")
end = text.find("    private async Task NavigateToReferenceAsync", start)
if start < 0 or end < 0:
    raise RuntimeError("RefreshCommitFilesAsync block not found")
replacement = r'''    private Task RefreshCommitFilesAsync()
    {
        var files = _viewModel.SelectedChangedFiles;
        _commitFiles.Clear();
        FilesTab.Header = files.Count == 0 ? "Changes" : $"Changes ({files.Count})";

        foreach (var file in files)
            _commitFiles.Add(new CommitFileRow(file.Status, file));

        var selected = _commitFiles.FirstOrDefault(row => ReferenceEquals(row.File, _viewModel.SelectedFile)) ?? _commitFiles.FirstOrDefault();
        CommitFilesList.SelectedItem = selected;
        return Task.CompletedTask;
    }

'''
text = text[:start] + replacement + text[end:]
write(page, text)

write("src/CSharpGit.Presentation/MainPage.LoadingOverlays.cs", r'''using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _loadingOverlaysInitialized;
    private Grid? _changesLoadingOverlay;
    private Grid? _diffLoadingOverlay;
    private ProgressRing? _changesLoadingRing;
    private ProgressRing? _diffLoadingRing;

    private void InitializeLoadingOverlays()
    {
        if (_loadingOverlaysInitialized || _viewModel is null) return;
        if (ChangedFilesTree.Parent is not Grid filesViewer) return;
        if (CompactDiffList.Parent is not Grid diffViewer) return;

        _changesLoadingOverlay = CreateLoadingOverlay("Loading changes…", out _changesLoadingRing);
        Grid.SetRow(_changesLoadingOverlay, 1);
        filesViewer.Children.Add(_changesLoadingOverlay);

        _diffLoadingOverlay = CreateLoadingOverlay("Loading diff…", out _diffLoadingRing);
        diffViewer.Children.Add(_diffLoadingOverlay);

        _viewModel.PropertyChanged += LoadingOverlayViewModel_PropertyChanged;
        _loadingOverlaysInitialized = true;
        UpdateLoadingOverlays();
    }

    private Grid CreateLoadingOverlay(string message, out ProgressRing progressRing)
    {
        progressRing = new ProgressRing
        {
            Width = 24,
            Height = 24
        };

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8
        };
        content.Children.Add(progressRing);
        content.Children.Add(new TextBlock
        {
            Text = message,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var overlay = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = RootLayout.Background
        };
        overlay.Children.Add(content);
        return overlay;
    }

    private void LoadingOverlayViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenRepositoryViewModel.IsChangedFilesLoading)
            or nameof(OpenRepositoryViewModel.IsDiffLoading))
            UpdateLoadingOverlays();
    }

    private void UpdateLoadingOverlays()
    {
        if (!_loadingOverlaysInitialized) return;

        _changesLoadingOverlay!.Visibility = _viewModel.ChangedFilesLoadingVisibility;
        _changesLoadingRing!.IsActive = _viewModel.IsChangedFilesLoading;
        _diffLoadingOverlay!.Visibility = _viewModel.DiffLoadingVisibility;
        _diffLoadingRing!.IsActive = _viewModel.IsDiffLoading;
    }

    private void DetachLoadingOverlays()
    {
        if (!_loadingOverlaysInitialized) return;
        _viewModel.PropertyChanged -= LoadingOverlayViewModel_PropertyChanged;
    }
}
''')

# File actions must not perform hidden Git work merely because selection changes.
file_open = "src/CSharpGit.Presentation/MainPage.FileOpening.cs"
text = read(file_open)
text = text.replace("or nameof(OpenRepositoryViewModel.SelectedCommit)", "or nameof(OpenRepositoryViewModel.SelectedHistoryRow)")
start = text.find("    private async Task RefreshCommitFileActionStateAsync()")
end = text.find("    private async Task RefreshWorkingTreeFileActionStateAsync()", start)
if start < 0 or end < 0:
    raise RuntimeError("RefreshCommitFileActionStateAsync block not found")
refresh = r'''    private Task RefreshCommitFileActionStateAsync()
    {
        Interlocked.Increment(ref _commitFileActionGeneration);
        _commitFileVersions = null;
        _commitRevealPath = null;

        var repository = _viewModel.Repository;
        var file = _viewModel.SelectedFile;
        if (repository is not null && file is not null && _repositoryPathService is not null)
            _commitRevealPath = TryResolveReveal(repository, file.Path);

        UpdateCommitButtons();
        return Task.CompletedTask;
    }

'''
text = text[:start] + refresh + text[end:]
start = text.find("    private void UpdateCommitButtons()")
end = text.find("    private void UpdateWorkingTreeButtons()", start)
if start < 0 or end < 0:
    raise RuntimeError("UpdateCommitButtons block not found")
update_buttons = r'''    private void UpdateCommitButtons()
    {
        if (_commitFileVersions is not null)
        {
            SetVersionButton(_commitOpenOriginalButton, _commitFileVersions.Original, "No original version is available.");
            SetVersionButton(_commitOpenChangedButton, _commitFileVersions.Changed, "No changed version is available.");
        }
        else
        {
            var file = _viewModel.SelectedFile;
            SetAvailabilityButton(
                _commitOpenOriginalButton,
                file is not null && !string.Equals(file.Status, "A", StringComparison.Ordinal),
                "Open original version",
                "No original version is available.");
            SetAvailabilityButton(
                _commitOpenChangedButton,
                file is not null && !string.Equals(file.Status, "D", StringComparison.Ordinal),
                "Open changed version",
                "No changed version is available.");
        }
        SetRevealButton(_commitRevealButton, _commitRevealPath);
    }

    private static void SetAvailabilityButton(Button? button, bool enabled, string enabledText, string disabledText)
    {
        if (button is null) return;
        button.IsEnabled = enabled;
        ToolTipService.SetToolTip(button, enabled ? enabledText : disabledText);
    }

'''
text = text[:start] + update_buttons + text[end:]
text = text.replace("var commit = _viewModel.SelectedCommit;", "var commit = _viewModel.SelectedHistoryRow?.Commit;")
text = text.replace("commit.Commit.Hash", "commit.Hash")
text = text.replace("_viewModel.SelectedCommit is null", "_viewModel.SelectedHistoryRow is null")
text = text.replace("_viewModel.SelectedCommit.Commit.Hash", "_viewModel.SelectedHistoryRow.Commit.Hash")
write(file_open, text)

# Durable product intent updated with the new strict-lazy model.
write(".idd/intent/IDD-0004.spec-history-and-diff.md", r'''# IDD-0004.spec-history-and-diff

## Intent

История commits, согласованный с ней graph, metadata выбранного commit и lazy Changes/diff дают пользователю центральное представление о развитии репозитория без предварительной загрузки всей истории и без Git work, которое не требуется видимой части UI.

## Related Specifications

- IDD-0001
- IDD-0003
- IDD-0006
- IDD-0009
- IDD-0019

## Behavior

- История показывает для commit как минимум graph, message, author, date/time, hash и refs.
- История отображает реальную topology: последовательности commits, branches и merges.
- Пользователь переключает scope как минимум между текущей branch и всеми доступными branches/refs; активный scope явно виден.
- История поддерживает filtering без требования расширенного query language.
- `HistoryRow.Commit` уже содержит full message, author, date/time, hash, parents и refs. При выборе history row эти metadata отображаются синхронно из уже загруженной history и не требуют Git CLI process.
- Нижняя details pane имеет `Commit` и `Changes`. Выбор commit сам по себе не означает запрос changed files или diff.
- Пока активен `Commit`, перемещение selection между уже загруженными history rows не запускает Git для commit details, changed files или diff и не показывает loading overlay.
- Changed files выбранного commit загружаются независимо и lazy только когда `Changes` активна. Сам факт создания/`Loaded` controls не делает Changes активной.
- Changed files представлены как иерархическое дерево repository-relative paths. File nodes показывают status и доступную added/removed statistics; folder nodes могут агрегировать statistics.
- После загрузки changed files допустимо выбрать первый file; diff загружается только для одного selected file и только пока `Changes` активна.
- Changed-files tree и selected-file diff используют одинаковую comparison base: первый parent обычного/merge commit; root commit сравнивается с empty tree/root semantics.
- Rename/copy сохраняют current path в `Path`, исходный path в `OriginalPath`; поддерживаются как минимум A/M/D/R/C и binary statistics.
- При смене selected file stale diff немедленно перестает отображаться, tree остается доступным, а loading overlay перекрывает только diff viewer.
- Compact unified diff различает added/removed/context/hunk rows, показывает old/new line numbers и скрывает служебный шум `diff/index/---/+++` из основного presentation.
- Binary file обозначается как binary вместо попытки текстового отображения.

## Durable Architecture And Constraints

- История загружается incrementally/lazily; graph строится из тех же history data.
- Commit selection является дешевой UI operation: metadata берется из `SelectedHistoryRow.Commit` и не зависит от `CommitDetails`/`ReadCommitAsync`.
- Changed files имеют отдельную lazy operation, возвращающую полный `ChangedFile` model (status, current/original paths, added/removed lines, binary) максимум одним Git CLI invocation на cache miss. Presentation не выполняет отдельный status query.
- Selected-file diff имеет отдельную lazy operation и максимум один Git CLI invocation на cache miss.
- Parent не определяется отдельной Git-командой в changed-files/diff hot path: он берется из `SelectedHistoryRow.Commit.Parents`.
- Interactive changed-files/diff path может использовать rename/copy detection, но не использует `--find-copies-harder`.
- Быстрая navigation при активной `Changes` debounce-ится примерно на 120 ms. Явное открытие `Changes` для уже выбранного commit может начать initial load сразу.
- Changed-files и diff operations имеют независимые `CancellationTokenSource` и generation/identity guards. Смена commit/file, уход с Changes и repository switch отменяют ненужную работу.
- Cancellation запущенного Git process завершает process tree и дожидается фактического exit/drain redirected streams; `OperationCanceledException` не показывается как пользовательская ошибка.
- Generation guards остаются обязательны даже при process cancellation: stale completion не может публиковать files/diff, менять selection/loading state или показывать error.
- Changed-files и просмотренные diffs имеют bounded session-level LRU caches. Cache key включает repository identity, commit, comparison parent/base; diff key дополнительно включает path. Cache hit не запускает Git.
- Repository switch отменяет current changed-files/diff work и очищает session caches.
- Async changed-files/diff loading не переводит приложение в global Busy и не блокирует history navigation.
- `Loading changes…` относится только к Changes/file-tree surface; `Loading diff…` — только к diff area. `Loading commit…` отсутствует в обычной history navigation.
- Selection-driven UI preparation (например состояние кнопок historical file actions) не должна скрыто запускать Git. Дополнительный Git разрешен только после явного пользовательского file action.
- Debug diagnostics фиксируют duration/cache hit|miss для changed-files/diff и Git operation duration, не логируя полный diff output.
- Background prefetch changed files/diffs не входит в текущую реализацию: `not visible = not loaded`.

## Performance Budget

- `Commit` active + select already-loaded commit: **0 Git processes**.
- `Changes` active + changed-files cache miss after debounce: **<= 1 Git process**.
- Select file + diff cache miss: **<= 1 additional Git process**.
- Changed-files/diff cache hit: **0 Git processes**.
- Нет дополнительных processes для metadata, parent lookup, status lookup или first-parent resolution.

## Non-Goals

- Изменение commit graph/topology, history ordering/incremental loading или branch/ref navigation.
- Working-tree diff/staging/commit/rebase/merge workflows.
- Background prefetch в первой реализации.
- `--find-copies-harder` в interactive navigation path.
- Persistent disk cache changed files/diffs.
- Специализированные viewers для binary formats или обязательный side-by-side diff.

## Acceptance Criteria

- Metadata выбранного commit появляется сразу из `HistoryRow.Commit`; `ReadCommitAsync` не участвует в обычной history navigation.
- На `Commit` любое перемещение между уже загруженными rows создает 0 Git CLI processes.
- Changed files не загружаются, пока `Changes` не активна.
- Changed-file model на cache miss получается максимум одним Git process и presentation не запускает отдельный `ReadFileStatusesAsync`.
- Diff одного selected file требует максимум одного дополнительного Git process.
- Fast ↑/↓ navigation не создает очередь Git processes; после остановки публикуется только актуальный commit.
- Obsolete operations реально cancel/terminate subprocess и дополнительно защищены generation checks.
- First-parent semantics согласованы между changed-files и diff; root commit работает без parent lookup.
- Rename/copy current/original paths, status/statistics и binary state корректны; `--find-copies-harder` отсутствует в hot path.
- Возврат к недавно просмотренному commit/file использует bounded cache и 0 Git processes.
- Stale files/diff никогда не показываются как данные нового commit/file.
- `Loading commit…` отсутствует; local Changes/Diff loading overlays не включают global Busy.
- IDD intent и implementation не противоречат друг другу.

## Verification

- Contract/UI tests проверяют direct `SelectedHistoryRow.Commit` bindings, отсутствие navigation `ReadCommitAsync`/status query, активность Pivot `Changes`, local loading surfaces и lazy lifecycle.
- ViewModel tests/contract checks покрывают cache, 120 ms debounce, independent CTS, generation protection и repository-session reset.
- Git integration tests покрывают added/modified/deleted/renamed/copied/binary, root и merge first-parent semantics; changed-files и selected-file diff используют один comparison parent.
- Low-level process-runner test отменяет deliberately long-running subprocess и проверяет, что cancellation task завершается после process termination без hanging redirected streams.
- Existing graph/history, changed-file tree и compact diff tests продолжают проходить.

## Reusable compact diff presentation

The compact unified-diff presentation (OLD/NEW line numbers, hunks, context/add/remove highlighting, binary state, and standard Git-header suppression) is reusable product UI. Commit Changes and the selected-file Working Tree diff use the same `FileDiff -> DiffLine -> CompactDiffLine` semantics and the same compact item template/resources.
''')

# Rewrite stale source-contract tests around product behavior rather than the old CommitDetails lifecycle.
write("tests/CSharpGit.Application.Tests/HistoryDiffUiContractTests.cs", r'''namespace CSharpGit.Application.Tests;

public sealed class HistoryDiffUiContractTests
{
    [Fact]
    public void HistoryDetailsUseHierarchicalChangesBesideSharedDenseDiff()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));
        var changes = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs"));
        var tree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "ChangedFileTreeNode.cs"));
        var diff = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "CompactDiffLine.cs"));
        var commitChangesSurface = ExtractCommitChangesSurface(xaml);

        Assert.Contains("x:Name=\"ChangedFilesTree\"", commitChangesSurface);
        Assert.Contains("x:Name=\"CompactDiffList\"", commitChangesSurface);
        Assert.Contains("ItemContainerStyle=\"{StaticResource DiffRowStyle}\"", commitChangesSurface);
        Assert.Contains("ItemTemplate=\"{StaticResource DiffItemTemplate}\"", commitChangesSurface);
        Assert.DoesNotContain("<PivotItem Header=\"Diff\">", xaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding SelectedCommit.Files}\"", xaml);
        Assert.Contains("ChangedFileTreeNode.Build", changes);
        Assert.Contains("CompactDiffLine.Build", changes);
        Assert.Contains("while (entry is null && children.Count == 1", tree);
        Assert.Contains("TryReadHunkStarts", diff);
        Assert.Contains("x:Key=\"DiffItemTemplate\"", workspace);
    }

    [Fact]
    public void CommitMetadataComesDirectlyFromSelectedHistoryRowWithoutCommitLoad()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));

        Assert.Contains("SelectedHistoryRow.Commit.Message", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.References", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.Author", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.AuthoredAt", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.Hash", xaml);
        Assert.Contains("SelectedHistoryRow.Commit.ParentsDisplay", xaml);
        Assert.DoesNotContain("SelectedCommit.Commit", xaml);
        Assert.DoesNotContain("LoadCommitAsync", viewModel);
        Assert.DoesNotContain("_historyService.ReadCommitAsync", viewModel);
        Assert.DoesNotContain("IsCommitLoading", viewModel);
    }

    [Fact]
    public void ChangesActivityUsesPivotSelectionAndNoSeparateStatusQuery()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var changes = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs"));

        Assert.Contains("SelectionChanged=\"DetailsTabs_SelectionChanged\"", xaml);
        Assert.Contains("SetChangesViewActive", changes);
        Assert.Contains("ReferenceEquals(DetailsTabs.SelectedItem, FilesTab)", changes);
        Assert.DoesNotContain("ReadFileStatusesAsync", page);
        Assert.Contains("new CommitFileRow(file.Status, file)", page);
    }

    [Fact]
    public void ChangedFilesAndDiffHaveIndependentLazyCancellationCacheAndLocalLoading()
    {
        var root = FindRepositoryRoot();
        var lazy = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.CommitChanges.cs"));
        var overlays = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.LoadingOverlays.cs"));

        Assert.Contains("ChangedFilesDebounceMilliseconds = 120", lazy);
        Assert.Contains("_changedFilesLoadCts", lazy);
        Assert.Contains("_diffLoadCts", lazy);
        Assert.Contains("BoundedLruCache", lazy);
        Assert.Contains("ReadChangedFilesAsync", lazy);
        Assert.Contains("ReadDiffAsync", lazy);
        Assert.Contains("IsCurrentChangedFilesRequest", lazy);
        Assert.Contains("IsCurrentDiffRequest", lazy);
        Assert.Contains("ResetCommitChangesSession", lazy);
        Assert.Contains("Loading changes…", overlays);
        Assert.Contains("Loading diff…", overlays);
        Assert.DoesNotContain("Loading commit…", overlays);
        Assert.DoesNotContain("IsBusy", overlays);
    }

    [Fact]
    public void GitHotPathUsesOneCombinedChangedFilesProcessAndNoHardCopySearch()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitFileAwareHistoryService.cs"));
        var runner = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitProcessRunner.cs"));

        Assert.Contains("\"--raw\", \"--numstat\"", service);
        Assert.Contains("\"--find-renames\", \"--find-copies\"", service);
        Assert.DoesNotContain("--find-copies-harder", service);
        Assert.Contains("string? parentHash", service);
        Assert.Contains("ChangedFile file", service);
        Assert.Contains("process.Kill(entireProcessTree: true)", runner);
        Assert.Contains("Task.WhenAll(outputTask, errorTask)", runner);
    }

    [Fact]
    public void HistoricalFileActionStateDoesNotResolveGitOnSelection()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.FileOpening.cs"));
        var stateMethod = ExtractBetween(source, "private Task RefreshCommitFileActionStateAsync()", "private async Task RefreshWorkingTreeFileActionStateAsync()");

        Assert.DoesNotContain("ResolveCommitAsync", stateMethod);
        Assert.Contains("SelectedFile", stateMethod);
        Assert.Contains("TryResolveReveal", stateMethod);
        Assert.Contains("SelectedHistoryRow", source);
        Assert.DoesNotContain("SelectedCommit", source);
    }

    private static string ExtractBetween(string value, string startMarker, string endMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = value.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start);
        return value[start..end];
    }

    private static string ExtractCommitChangesSurface(string xaml)
    {
        const string startMarker = "<PivotItem x:Name=\"FilesTab\" Header=\"Changes\">";
        const string endMarker = "</PivotItem>";
        var start = xaml.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = xaml.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start);
        return xaml[start..(end + endMarker.Length)];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
''')

write("tests/CSharpGit.Git.Tests/GitProcessRunnerTests.cs", r'''using System.Diagnostics;

namespace CSharpGit.Git.Tests;

public sealed class GitProcessRunnerTests
{
    [Fact]
    public async Task CancellationTerminatesLongRunningSubprocessAndCompletesPipeReaders()
    {
        var executable = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
            : "/bin/sh";
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "/d", "/s", "/c", "ping -n 30 127.0.0.1 >nul" }
            : new[] { "-c", "sleep 30" };
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var task = GitProcessRunner.RunProcessAsync(
            executable,
            Path.GetTempPath(),
            "CancellationTest",
            cancellation.Token,
            arguments,
            processId => started.TrySetResult(processId));
        var processId = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var completion = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(8)));
        Assert.Same(task, completion);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);

        try
        {
            using var process = Process.GetProcessById(processId);
            Assert.True(process.HasExited, "Cancelled subprocess is still running.");
        }
        catch (ArgumentException)
        {
            // Process id no longer exists, which is the expected outcome.
        }
    }
}
''')

write("tests/CSharpGit.Git.Tests/CommitChangesServiceTests.cs", r'''using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class CommitChangesServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-commit-changes-{Guid.NewGuid():N}");
    private readonly GitCliRepositoryService _repositoryService = new();
    private readonly GitRepositoryFileVersionService _versionService = new();

    [Fact]
    public async Task RootAndRenameUseOneProcessPerLazyOperationAndPreservePaths()
    {
        var repository = await CreateRepositoryAsync();
        CommitText("root.txt", "root\n", "root");
        var root = RunGitOutput("rev-parse", "HEAD");
        var service = CreateService();

        var beforeRoot = service.GitInvocationCount;
        var rootFiles = await service.ReadChangedFilesAsync(repository, root, null);
        Assert.Equal(beforeRoot + 1, service.GitInvocationCount);
        var rootFile = Assert.Single(rootFiles);
        Assert.Equal("A", rootFile.Status);
        Assert.Equal("root.txt", rootFile.Path);

        var lines = string.Join('\n', Enumerable.Range(1, 40).Select(index => $"line {index}")) + "\n";
        CommitText("old name.txt", lines, "base rename");
        RunGit("mv", "old name.txt", "new name.txt");
        RunGit("commit", "-m", "rename");
        var commit = RunGitOutput("rev-parse", "HEAD");
        var parent = RunGitOutput("rev-parse", "HEAD^1");

        var beforeFiles = service.GitInvocationCount;
        var files = await service.ReadChangedFilesAsync(repository, commit, parent);
        Assert.Equal(beforeFiles + 1, service.GitInvocationCount);
        var renamed = Assert.Single(files);
        Assert.Equal("R", renamed.Status);
        Assert.Equal("old name.txt", renamed.OriginalPath);
        Assert.Equal("new name.txt", renamed.Path);

        var beforeDiff = service.GitInvocationCount;
        var diff = await service.ReadDiffAsync(repository, commit, parent, renamed);
        Assert.Equal(beforeDiff + 1, service.GitInvocationCount);
        var text = string.Join('\n', diff.Lines.Select(line => line.Text));
        Assert.Contains("rename from old name.txt", text);
        Assert.Contains("rename to new name.txt", text);
    }

    [Fact]
    public async Task ChangedFilesCoverAddModifyDeleteCopyAndBinary()
    {
        var repository = await CreateRepositoryAsync();
        var source = string.Join('\n', Enumerable.Range(1, 50).Select(index => $"source {index}")) + "\n";
        CommitText("source.txt", source, "base");
        CommitText("delete.txt", "delete me\n", "delete base");
        CommitText("modify.txt", "old\n", "modify base");
        CommitBytes("binary.bin", [0, 1, 2, 0, 255], "binary base");
        var parent = RunGitOutput("rev-parse", "HEAD");

        File.Copy(Path.Combine(_temporaryDirectory, "source.txt"), Path.Combine(_temporaryDirectory, "copy.txt"));
        File.AppendAllText(Path.Combine(_temporaryDirectory, "source.txt"), "source changed\n");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "modify.txt"), "new\n");
        File.Delete(Path.Combine(_temporaryDirectory, "delete.txt"));
        File.WriteAllText(Path.Combine(_temporaryDirectory, "added.txt"), "added\n");
        File.WriteAllBytes(Path.Combine(_temporaryDirectory, "binary.bin"), [0, 1, 3, 0, 254]);
        RunGit("add", "-A");
        RunGit("commit", "-m", "mixed changes");
        var commit = RunGitOutput("rev-parse", "HEAD");

        var service = CreateService();
        var files = await service.ReadChangedFilesAsync(repository, commit, parent);
        Assert.Contains(files, file => file.Status == "A" && file.Path == "added.txt");
        Assert.Contains(files, file => file.Status == "M" && file.Path == "modify.txt");
        Assert.Contains(files, file => file.Status == "D" && file.Path == "delete.txt");
        Assert.Contains(files, file => file.Status == "C" && file.Path == "copy.txt" && file.OriginalPath == "source.txt");
        Assert.Contains(files, file => file.Path == "binary.bin" && file.IsBinary);
    }

    [Fact]
    public async Task MergeUsesExplicitFirstParentForFilesAndDiff()
    {
        var repository = await CreateRepositoryAsync();
        CommitText("base.txt", "base\n", "base");
        RunGit("checkout", "-b", "feature");
        CommitText("feature.txt", "feature\n", "feature");
        RunGit("checkout", "main");
        CommitText("main.txt", "main\n", "main");
        RunGit("merge", "--no-ff", "feature", "-m", "merge feature");
        var merge = RunGitOutput("rev-parse", "HEAD");
        var firstParent = RunGitOutput("rev-parse", "HEAD^1");

        var service = CreateService();
        var files = await service.ReadChangedFilesAsync(repository, merge, firstParent);
        var feature = Assert.Single(files, file => file.Path == "feature.txt");
        var diff = await service.ReadDiffAsync(repository, merge, firstParent, feature);
        Assert.Contains(diff.Lines, line => line.Text.Contains("+feature", StringComparison.Ordinal));
    }

    private GitFileAwareHistoryService CreateService() =>
        new(new GitReferenceHistoryService(), _versionService);

    private async Task<Repository> CreateRepositoryAsync()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "tests@example.invalid");
        RunGit("config", "user.name", "CSharpGit Tests");
        return await _repositoryService.OpenAsync(_temporaryDirectory);
    }

    private void CommitText(string path, string contents, string message)
    {
        var fullPath = Path.Combine(_temporaryDirectory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        RunGit("add", "--", path);
        RunGit("commit", "-m", message);
    }

    private void CommitBytes(string path, byte[] contents, string message)
    {
        File.WriteAllBytes(Path.Combine(_temporaryDirectory, path), contents);
        RunGit("add", "--", path);
        RunGit("commit", "-m", message);
    }

    private string RunGitOutput(params string[] arguments)
    {
        var (output, _) = RunGitCore(arguments);
        return output.Trim();
    }

    private void RunGit(params string[] arguments) => RunGitCore(arguments);

    private (string Output, string Error) RunGitCore(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _temporaryDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {output}\n{error}");
        return (output, error);
    }

    public void Dispose() => TestDirectory.Delete(_temporaryDirectory);
}
''')

print("History navigation refactor prepared")
