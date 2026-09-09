using System.Diagnostics;
using System.Text.Json;

namespace CSharpGit.Desktop.Tests;

public sealed class DesktopApplicationTests
{
    [Fact]
    public async Task LaunchesRealApplicationForRepositoryAndLinkedWorktree()
    {
        var application = FindApplication();
        if (!HasDesktopSession() || !CanUseDesktopApplicationData()) return;
        Assert.True(application is not null, "Build CSharpGit.Presentation first, or set CSHARPGIT_DESKTOP_APP to its executable.");

        await using var fixture = await GitFixture.CreateAsync();
        await RunCheckAsync(application!, fixture.Repository, false, fixture.Home);
        await RunCheckAsync(application!, fixture.Worktree, true, fixture.Home);
        await RunNormalCloseCheckAsync(application!, fixture.Repository, fixture.Home);
    }

    private static async Task RunCheckAsync(string application, string repository, bool worktree, string isolatedHome)
    {
        var result = Path.Combine(isolatedHome, $"ui-{Guid.NewGuid():N}.json");
        var start = CreateStartInfo(application, repository, isolatedHome);
        start.Environment["CSHARPGIT_UI_CHECK_REPOSITORY"] = repository;
        start.Environment["CSHARPGIT_UI_CHECK_RESULT"] = result;
        start.Environment["CSHARPGIT_UI_CHECK_WORKTREE"] = worktree ? "1" : "0";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Desktop application could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
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

        Assert.True(File.Exists(result), $"Desktop application exited with {process.ExitCode} without a result.{Environment.NewLine}{await standardOutput}{Environment.NewLine}{await standardError}");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(result));
        var failures = document.RootElement.GetProperty("Failures").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.True(document.RootElement.GetProperty("Passed").GetBoolean(), string.Join(Environment.NewLine, failures));
        Assert.Equal(worktree, document.RootElement.GetProperty("IsWorktree").GetBoolean());
    }

    private static async Task RunNormalCloseCheckAsync(string application, string repository, string isolatedHome)
    {
        var start = CreateStartInfo(application, repository, isolatedHome);
        start.Environment["CSHARPGIT_UI_CHECK_REPOSITORY"] = repository;
        start.Environment["CSHARPGIT_CLOSE_CHECK"] = "1";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Desktop application could not be started for the close check.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var timedOut = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync();
            }
        }

        var output = await standardOutput;
        var error = await standardError;
        Assert.False(timedOut, $"Desktop application hung while closing normally.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        Assert.Equal(0, process.ExitCode);
    }

    private static ProcessStartInfo CreateStartInfo(string application, string repository, string isolatedHome)
    {
        var start = new ProcessStartInfo(application)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repository
        };
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(isolatedHome, ".gitconfig");
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["HOME"] = isolatedHome;
        start.Environment["XDG_CONFIG_HOME"] = isolatedHome;
        start.Environment["LOCALAPPDATA"] = isolatedHome;
        start.Environment["APPDATA"] = isolatedHome;
        return start;
    }

    private static string? FindApplication()
    {
        var root = FindRepositoryRoot();
        var name = OperatingSystem.IsWindows() ? "CSharpGit.Presentation.exe" : "CSharpGit.Presentation";
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            // BuildDesktopApplication has just built this exact configuration.
            // Prefer it over an inherited override, which may point to an older
            // artifact and silently execute stale runtime assertions.
            var output = Path.Combine(root, "src", "CSharpGit.Presentation", "bin", configuration, "net10.0-desktop", name);
            if (File.Exists(output)) return output;
        }

        var configured = Environment.GetEnvironmentVariable("CSHARPGIT_DESKTOP_APP");
        return File.Exists(configured) ? Path.GetFullPath(configured) : null;
    }

    private static bool HasDesktopSession() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ||
        (OperatingSystem.IsLinux() && (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))));

    private static bool CanUseDesktopApplicationData()
    {
        if (!OperatingSystem.IsWindows()) return true;

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CSharpGit.Presentation");
        var probe = Path.Combine(directory, $"ui-check-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }

    private sealed class GitFixture : IAsyncDisposable
    {
        private GitFixture(string root, string repository, string worktree, string home) => (Root, Repository, Worktree, Home) = (root, repository, worktree, home);
        public string Root { get; }
        public string Repository { get; }
        public string Worktree { get; }
        public string Home { get; }

        public static async Task<GitFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "csharpgit-ui", Guid.NewGuid().ToString("N"));
            var repository = Path.Combine(root, "repository");
            var worktree = Path.Combine(root, "linked-worktree");
            var home = Path.Combine(root, "home");
            Directory.CreateDirectory(repository);
            Directory.CreateDirectory(home);
            // The application reads the explicitly isolated global Git config.
            // Git treats a missing GIT_CONFIG_GLOBAL file as an error when that
            // file is queried directly, so provide a deterministic empty config.
            await File.WriteAllTextAsync(Path.Combine(home, ".gitconfig"), string.Empty);
            await GitAsync(repository, home, "init");
            await GitAsync(repository, home, "config", "user.name", "Desktop Check");
            await GitAsync(repository, home, "config", "user.email", "desktop@example.invalid");
            await File.WriteAllTextAsync(Path.Combine(repository, "tracked.txt"), "initial\n");
            await GitAsync(repository, home, "add", "tracked.txt");
            await GitAsync(repository, home, "commit", "-m", "Initial English commit");
            await GitAsync(repository, home, "worktree", "add", "-b", "ui-worktree", worktree);
            await File.WriteAllTextAsync(Path.Combine(repository, "unstaged.txt"), "empty index choice\n");
            await File.WriteAllTextAsync(Path.Combine(worktree, "unstaged.txt"), "empty index choice\n");
            return new GitFixture(root, repository, worktree, home);
        }

        private static async Task GitAsync(string workingDirectory, string home, params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardError = true, WorkingDirectory = workingDirectory };
            start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(home, ".gitconfig");
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["HOME"] = home;
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not be started.");
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException(error);
        }

        public async ValueTask DisposeAsync()
        {
            try { await GitAsync(Repository, Home, "worktree", "remove", "--force", Worktree); } catch { }
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    ClearReadOnlyAttributes(Root);
                    Directory.Delete(Root, true);
                    return;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }

                // Git and the desktop runtime can release their final handles a
                // little after process exit on Windows.
                await Task.Delay(100 * (attempt + 1));
            }
        }

        private static void ClearReadOnlyAttributes(string path)
        {
            if (!Directory.Exists(path)) return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }
    }
}
