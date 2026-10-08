using System.Diagnostics;
using System.Text.Json;

namespace CSharpGit.Git.Tests;

internal static class GitTestMeasurements
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("CSHARPGIT_TEST_METRICS") == "1";
    private static readonly AsyncLocal<string?> Phase = new();
    private static readonly object Sync = new();
    private static readonly string? OutputDirectory = Environment.GetEnvironmentVariable("CSHARPGIT_TEST_METRICS_DIR");
    private static readonly List<string> Pending = [];

    static GitTestMeasurements()
    {
        if (Enabled && !string.IsNullOrEmpty(OutputDirectory))
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    public static IDisposable BeginPhase(string phase)
    {
        var previous = Phase.Value;
        Phase.Value = phase;
        return new PhaseScope(previous);
    }

    public static T Measure<T>(string kind, string phase, Func<T> work)
    {
        if (!Enabled) return work();
        var watch = Stopwatch.StartNew();
        try { return work(); }
        finally { Record(kind, phase, watch.Elapsed); }
    }

    public static void Record(string kind, string? phase, TimeSpan elapsed)
    {
        if (!Enabled || string.IsNullOrEmpty(OutputDirectory)) return;
        var line = JsonSerializer.Serialize(new
        {
            kind,
            phase = phase ?? Phase.Value ?? "test-helper",
            elapsed_ms = elapsed.TotalMilliseconds
        });
        lock (Sync)
        {
            Pending.Add(line);
            if (Pending.Count >= 128) FlushUnderLock();
        }
    }

    private static void Flush()
    {
        lock (Sync) FlushUnderLock();
    }

    private static void FlushUnderLock()
    {
        if (Pending.Count == 0 || string.IsNullOrEmpty(OutputDirectory)) return;
        Directory.CreateDirectory(OutputDirectory);
        File.AppendAllText(
            Path.Combine(OutputDirectory, $"git-tests-{Environment.ProcessId}.jsonl"),
            string.Join("\n", Pending) + "\n");
        Pending.Clear();
    }

    private sealed class PhaseScope(string? previous) : IDisposable
    {
        public void Dispose() => Phase.Value = previous;
    }
}
