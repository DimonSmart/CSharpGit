using System.Diagnostics;

namespace CSharpGit.Git.Tests;

/// <summary>Runs real Git in an isolated child-process environment; no process-global Git config changes.</summary>
internal static class TestGitRunner
{
    private static readonly string EmptyGlobalConfig = Path.Combine(
        Path.GetTempPath(), $"csharpgit-test-global-{Guid.NewGuid():N}");

    public static string Run(string directory, params string[] arguments)
    {
        var result = TryRun(directory, arguments);
        if (!result.Success)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.Error}");
        return result.Output.Trim();
    }

    public static (bool Success, string Output, string Error) TryRun(string directory, params string[] arguments) =>
        Execute(directory, arguments, null);

    public static string RunWithInput(string directory, string input, params string[] arguments)
    {
        var result = Execute(directory, arguments, input);
        if (!result.Success)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.Error}");
        return result.Output.Trim();
    }

    private static (bool Success, string Output, string Error) Execute(
        string directory, IReadOnlyList<string> arguments, string? input)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // The tests do not inherit personal aliases, credential helpers, autocrlf, signing or system config.
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = EmptyGlobalConfig;
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in arguments) start.ArgumentList.Add(arg);

        var watch = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (input is null)
            {
                process.StandardInput.Close();
            }
            else
            {
                // Write concurrently so fast-import can consume stdin while stdout/stderr are drained.
                var inputTask = Task.Run(async () =>
                {
                    await process.StandardInput.WriteAsync(input);
                    await process.StandardInput.FlushAsync();
                    process.StandardInput.Close();
                });
                if (!process.WaitForExit(120_000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                    throw new TimeoutException($"git {string.Join(' ', arguments)} timed out");
                }
                inputTask.GetAwaiter().GetResult();
                return (process.ExitCode == 0, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
            }

            if (!process.WaitForExit(120_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException($"git {string.Join(' ', arguments)} timed out");
            }
            return (process.ExitCode == 0, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
        }
        finally
        {
            GitTestMeasurements.Record("git-process", null, watch.Elapsed);
        }
    }
}
