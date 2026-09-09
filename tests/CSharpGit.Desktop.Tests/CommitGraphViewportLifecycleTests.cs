using System.Diagnostics;
using System.Text.Json;

namespace CSharpGit.Desktop.Tests;

public sealed class CommitGraphViewportLifecycleTests
{
    [Fact]
    public async Task GraphRemainsBoundToCurrentRowsAcrossViewportResizeAndRecycling()
    {
        var application = FindApplication();
        if (!HasDesktopSession() || application is null) return;

        var root = Path.Combine(Path.GetTempPath(), "csharpgit-graph-viewport", Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "repository");
        var home = Path.Combine(root, "home");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(Path.Combine(home, ".gitconfig"), string.Empty);

        try
        {
            await InitializeRepositoryAsync(repository, home);
            var result = Path.Combine(home, $"graph-viewport-{Guid.NewGuid():N}.json");
            var start = CreateStartInfo(application, repository, home);
            start.Environment["CSHARPGIT_UI_CHECK_REPOSITORY"] = repository;
            start.Environment["CSHARPGIT_UI_CHECK_RESULT"] = result;
            start.Environment["CSHARPGIT_UI_CHECK_WORKTREE"] = "0";
            start.Environment["CSHARPGIT_GRAPH_VIEWPORT_CHECK"] = "1";

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Desktop application could not be started.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
                await process.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync();
                }
            }

            Assert.True(File.Exists(result), $"Desktop application exited with {process.ExitCode} without a graph viewport result.{Environment.NewLine}{await standardOutput}{Environment.NewLine}{await standardError}");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(result));
            var failures = document.RootElement.GetProperty("Failures").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.True(document.RootElement.GetProperty("Passed").GetBoolean(), string.Join(Environment.NewLine, failures));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task InitializeRepositoryAsync(string repository, string home)
    {
        await GitAsync(repository, home, "init", "-b", "main");
        await GitAsync(repository, home, "config", "user.name", "Graph Viewport Check");
        await GitAsync(repository, home, "config", "user.email", "graph@example.invalid");

        await File.WriteAllTextAsync(Path.Combine(repository, "tracked.txt"), "root\n");
        await GitAsync(repository, home, "add", "tracked.txt");
        await GitAsync(repository, home, "commit", "-m", "Graph root");

        await GitAsync(repository, home, "switch", "-c", "graph-feature");
        await File.WriteAllTextAsync(Path.Combine(repository, "feature.txt"), "feature\n");
        await GitAsync(repository, home, "add", "feature.txt");
        await GitAsync(repository, home, "commit", "-m", "Graph feature");

        await GitAsync(repository, home, "switch", "main");
        await File.WriteAllTextAsync(Path.Combine(repository, "main.txt"), "main\n");
        await GitAsync(repository, home, "add", "main.txt");
        await GitAsync(repository, home, "commit", "-m", "Graph main before merge");
        await GitAsync(repository, home, "merge", "--no-ff", "graph-feature", "-m", "Graph merge");

        for (var index = 0; index < 125; index++)
        {
            await File.AppendAllTextAsync(Path.Combine(repository, "tracked.txt"), $"history {index}\n");
            await GitAsync(repository, home, "commit", "-am", $"Graph history {index:000}");
        }

        await GitAsync(repository, home, "tag", "graph-tag");
        await GitAsync(repository, home, "switch", "-c", "graph-side");
        await File.WriteAllTextAsync(Path.Combine(repository, "side.txt"), "side\n");
        await GitAsync(repository, home, "add", "side.txt");
        await GitAsync(repository, home, "commit", "-m", "Graph side tip");
        await GitAsync(repository, home, "switch", "main");

        await File.WriteAllTextAsync(Path.Combine(repository, "stash.txt"), "stash\n");
        await GitAsync(repository, home, "stash", "push", "-u", "-m", "graph viewport stash");
        await File.WriteAllTextAsync(Path.Combine(repository, "unstaged.txt"), "empty index choice\n");
    }

    private static ProcessStartInfo CreateStartInfo(string application, string repository, string home)
    {
        var start = new ProcessStartInfo(application)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repository
        };
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(home, ".gitconfig");
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["HOME"] = home;
        start.Environment["XDG_CONFIG_HOME"] = home;
        start.Environment["LOCALAPPDATA"] = home;
        start.Environment["APPDATA"] = home;
        return start;
    }

    private static async Task GitAsync(string workingDirectory, string home, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(home, ".gitconfig");
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["HOME"] = home;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not be started.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
    }

    private static string? FindApplication()
    {
        var root = FindRepositoryRoot();
        var name = OperatingSystem.IsWindows() ? "CSharpGit.Presentation.exe" : "CSharpGit.Presentation";
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            var output = Path.Combine(root, "src", "CSharpGit.Presentation", "bin", configuration, "net10.0-desktop", name);
            if (File.Exists(output)) return output;
        }

        var configured = Environment.GetEnvironmentVariable("CSHARPGIT_DESKTOP_APP");
        return File.Exists(configured) ? Path.GetFullPath(configured) : null;
    }

    private static bool HasDesktopSession() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ||
        (OperatingSystem.IsLinux() && (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
