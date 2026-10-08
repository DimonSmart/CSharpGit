using System.Collections.Concurrent;
using System.Diagnostics;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git.Tests;

/// <summary>
/// Test-only process profiler. It records categories and durations, never arguments,
/// working directories, configuration values, stdout, stderr or environment.
/// </summary>
internal sealed class ProfilingGitCommandActivitySink(IGitCommandActivitySink inner) : IGitCommandActivitySink
{
    private readonly ConcurrentDictionary<Guid, (Stopwatch Timer, string Category)> _started = new();

    public Guid Started(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandKind commandKind)
    {
        var id = inner.Started(executable, workingDirectory, arguments, commandKind);
        var category = arguments.Count > 0 ? arguments[0] switch
        {
            "config" => "config",
            "difftool" when arguments.Contains("--tool-help") => "difftool-help",
            "mergetool" when arguments.Contains("--tool-help") => "mergetool-help",
            "difftool" => "difftool",
            "mergetool" => "mergetool",
            _ => "other"
        } : "other";
        _started[id] = (Stopwatch.StartNew(), category);
        return id;
    }

    public void OutputReceived(Guid id, GitOutputStream stream, string chunk) =>
        inner.OutputReceived(id, stream, chunk);

    public void Completed(Guid id, int exitCode)
    {
        try { inner.Completed(id, exitCode); }
        finally { Finish(id); }
    }

    public void Cancelled(Guid id, int? exitCode)
    {
        try { inner.Cancelled(id, exitCode); }
        finally { Finish(id); }
    }

    private void Finish(Guid id)
    {
        if (!_started.TryRemove(id, out var item)) return;
        item.Timer.Stop();
        GitTestMeasurements.Record("git-service-process", item.Category, item.Timer.Elapsed);
    }
}
