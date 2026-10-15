using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace CSharpGit.Desktop.Tests;

public sealed class DesktopApplicationTests
{
    [SkippableFact]
    public async Task LaunchesRealApplicationForRepositoryAndLinkedWorktree()
    {
        var application = FindApplication();
        Skip.IfNot(HasDesktopSession(), "No supported desktop session is available.");
        Assert.True(application is not null, "Build CSharpGit.Presentation first, or set CSHARPGIT_DESKTOP_APP to its executable.");

        await using var fixture = await GitFixture.CreateAsync();
        await RunCheckAsync(application!, fixture.Repository, false, fixture.Home);
        await RunCheckAsync(application!, fixture.Worktree, true, fixture.Home);
        await RunNormalCloseCheckAsync(application!, fixture.Repository, fixture.Home);
        await RunNormalCloseCheckAsync(application!, fixture.Worktree, fixture.Home);
        await RunLifecycleCheckAsync(application!, fixture.Repository, fixture.Worktree, fixture.Home);
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
        var pipeName = $"csharpgit-close-{Guid.NewGuid():N}";
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = CreateStartInfo(application, repository, isolatedHome);
        start.Environment["CSHARPGIT_UI_CHECK_REPOSITORY"] = repository;
        start.Environment["CSHARPGIT_CLOSE_CHECK"] = "1";
        start.Environment["CSHARPGIT_CLOSE_CHECK_PIPE"] = pipeName;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Desktop application could not be started for the close check.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var phase = "startup/readiness";
        string? failure = null;
        try
        {
            using (var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
            {
                var readiness = WaitForCloseReadinessAsync(pipe, repository,
                    start.Environment["CSHARPGIT_APP_DATA_ROOT"]!, startupTimeout.Token);
                var exit = process.WaitForExitAsync(startupTimeout.Token);
                if (await Task.WhenAny(readiness, exit) == exit && !readiness.IsCompleted)
                {
                    startupTimeout.Cancel();
                    try { await readiness; } catch (OperationCanceledException) { }
                    throw new InvalidOperationException("Desktop application exited before reporting readiness.");
                }
                await readiness;
            }

            phase = "normal shutdown after readiness";
            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("Close".AsMemory(), shutdownTimeout.Token);
            await process.WaitForExitAsync(shutdownTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            failure = $"Timed out during {phase}.";
        }
        catch (Exception exception)
        {
            failure = exception.ToString();
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
        Assert.True(failure is null, $"Desktop close check failed during {phase}: {failure}{Environment.NewLine}{output}{Environment.NewLine}{error}");
        Assert.Equal(0, process.ExitCode);
        Assert.Contains("DesktopCloseCheck: CloseDispatched", output, StringComparison.Ordinal);
        Assert.Contains("DesktopCloseCheck: CloseAccepted", output, StringComparison.Ordinal);
        Assert.Contains("DesktopCloseCheck: WindowClosed", output, StringComparison.Ordinal);
        Assert.Contains("DesktopCloseCheck: HostStopped", output, StringComparison.Ordinal);
        Assert.DoesNotContain("DesktopCloseCheck: Failed", output, StringComparison.Ordinal);
        Assert.DoesNotContain("DesktopCloseCheck: CloseCanceled", output, StringComparison.Ordinal);
    }


    private static async Task RunLifecycleCheckAsync(
        string application, string original, string worktree, string isolatedHome)
    {
        var pipeName = $"csharpgit-lifecycle-{Guid.NewGuid():N}";
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = CreateStartInfo(application, original, isolatedHome);
        start.Environment["CSHARPGIT_UI_CHECK_REPOSITORY"] = original;
        start.Environment["CSHARPGIT_CLOSE_CHECK"] = "1";
        start.Environment["CSHARPGIT_CLOSE_CHECK_PIPE"] = pipeName;
        start.Environment["CSHARPGIT_CLOSE_CHECK_EXTENDED"] = "1";

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Desktop lifecycle test did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var ready = await reader.ReadLineAsync(timeout.Token);
            Assert.NotNull(ready);
            using (var d = JsonDocument.Parse(ready!))
                Assert.Equal("Ready", d.RootElement.GetProperty("State").GetString());

            async Task<JsonDocument> CommandAsync(string message)
            {
                await writer.WriteLineAsync(message.AsMemory(), timeout.Token);
                var response = await reader.ReadLineAsync(timeout.Token);
                Assert.NotNull(response);
                var d = JsonDocument.Parse(response!);
                Assert.Equal("Snapshot", d.RootElement.GetProperty("State").GetString());
                return d;
            }

            static void AssertOpened(JsonElement value, string path, bool isWorktree)
            {
                Assert.True(value.GetProperty("Success").GetBoolean(),
                    value.GetProperty("Error").GetString());
                Assert.Equal(Path.GetFullPath(path),
                    Path.GetFullPath(value.GetProperty("Path").GetString()!),
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                Assert.Equal(isWorktree, value.GetProperty("IsWorktree").GetBoolean());
                Assert.False(string.IsNullOrWhiteSpace(value.GetProperty("Head").GetString()));
                Assert.True(value.GetProperty("HasBaseline").GetBoolean());
                Assert.True(value.GetProperty("HistoryCount").GetInt32() > 0);
            }

            // Queue a real production monitor callback for the old session.
            using (var d = await CommandAsync("CaptureMonitorDelivery"))
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());

            using (var d = await CommandAsync("Switch\t" + worktree))
            {
                AssertOpened(d.RootElement, worktree, true);
                Assert.Equal("ui-worktree", d.RootElement.GetProperty("Branch").GetString());
            }

            using (var d = await CommandAsync("DeliverCapturedMonitor"))
                Assert.False(d.RootElement.GetProperty("RefreshRequired").GetBoolean());

            using (var d = await CommandAsync("SetDraft\tkeep this draft"))
                Assert.Equal("keep this draft", d.RootElement.GetProperty("Draft").GetString());

            using (var d = await CommandAsync("Switch\t" + Path.Combine(isolatedHome, "nonexistent-repo")))
            {
                Assert.False(d.RootElement.GetProperty("Success").GetBoolean());
                Assert.Equal("keep this draft", d.RootElement.GetProperty("Draft").GetString());
                AssertOpenedSnapshotPath(d.RootElement, worktree);
            }

            using (var d = await CommandAsync("SetDraft\t"))
                Assert.Equal(string.Empty, d.RootElement.GetProperty("Draft").GetString());

            using (var d = await CommandAsync("Switch\t" + original))
                AssertOpened(d.RootElement, original, false);

            // Same path as the original session, new Repository instance.
            using (var d = await CommandAsync("DeliverCapturedMonitor"))
            {
                AssertOpenedSnapshotPath(d.RootElement, original);
                Assert.False(d.RootElement.GetProperty("RefreshRequired").GetBoolean());
            }


            // A real B snapshot is read, then a presentation subscriber fails;
            // A must be restored and the draft cannot be discarded.
            using (var d = await CommandAsync("SetDraft\trollback draft"))
                Assert.Equal("rollback draft", d.RootElement.GetProperty("Draft").GetString());
            using (var d = await CommandAsync("Switch\t" + isolatedHome))
            {
                Assert.False(d.RootElement.GetProperty("Success").GetBoolean());
                AssertOpenedSnapshotPath(d.RootElement, original);
                Assert.Equal("rollback draft", d.RootElement.GetProperty("Draft").GetString());
            }
            using (var d = await CommandAsync("CancelOpenBeforeAssignment\t" + worktree))
            {
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
                AssertOpenedSnapshotPath(d.RootElement, original);
                Assert.Equal("rollback draft", d.RootElement.GetProperty("Draft").GetString());
                Assert.True(d.RootElement.GetProperty("CanChangeRepository").GetBoolean());
            }
            using (var d = await CommandAsync("ReadFailureAfterAssignment\t" + worktree))
            {
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
                AssertOpenedSnapshotPath(d.RootElement, original);
                Assert.Equal("rollback draft", d.RootElement.GetProperty("Draft").GetString());
                Assert.True(d.RootElement.GetProperty("HasBaseline").GetBoolean());
                Assert.False(d.RootElement.GetProperty("IsBusy").GetBoolean());
                Assert.True(d.RootElement.GetProperty("CanChangeRepository").GetBoolean());
                Assert.NotNull(d.RootElement.GetProperty("Error").GetString());
            }
            using (var d = await CommandAsync("FailNextPublication\t" + worktree + "\tInjected B publication failure"))
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
            using (var d = await CommandAsync("Switch\t" + worktree))
            {
                Assert.False(d.RootElement.GetProperty("Success").GetBoolean());
                AssertOpenedSnapshotPath(d.RootElement, original);
                Assert.Contains("Injected B publication failure", d.RootElement.GetProperty("Error").GetString());
                Assert.Equal("rollback draft", d.RootElement.GetProperty("Draft").GetString());
                Assert.True(d.RootElement.GetProperty("HasBaseline").GetBoolean());
                Assert.True(d.RootElement.GetProperty("CanChangeRepository").GetBoolean());
                Assert.False(d.RootElement.GetProperty("IsBusy").GetBoolean());
            }

            // Both the B publication and A recovery fail. This must report
            // both causes and clear any partly published state from B.
            using (var d = await CommandAsync("FailNextPublication\t" + worktree + "\tInjected B double failure"))
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
            using (var d = await CommandAsync("FailNextPublication\t" + original + "\tInjected A rollback failure"))
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
            using (var d = await CommandAsync("Switch\t" + worktree))
            {
                Assert.False(d.RootElement.GetProperty("Success").GetBoolean());
                AssertOpenedSnapshotPath(d.RootElement, original);
                Assert.Equal("rollback draft", d.RootElement.GetProperty("Draft").GetString());
                Assert.Contains("Injected B double failure", d.RootElement.GetProperty("Error").GetString());
                Assert.Contains("Injected A rollback failure", d.RootElement.GetProperty("Error").GetString());
                Assert.False(d.RootElement.GetProperty("HasBaseline").GetBoolean());
                Assert.Equal(0, d.RootElement.GetProperty("BranchCount").GetInt32());
                Assert.Equal(0, d.RootElement.GetProperty("TagCount").GetInt32());
                Assert.Equal(0, d.RootElement.GetProperty("StashCount").GetInt32());
                Assert.False(d.RootElement.GetProperty("IsBusy").GetBoolean());
                Assert.True(d.RootElement.GetProperty("CanChangeRepository").GetBoolean());
            }
            using (var d = await CommandAsync("Refresh"))
                AssertOpened(d.RootElement, original, false);

            // Cancellation after the new Repository was assigned is rolled back
            // and releases the switch gate, preserving the old draft.
            using (var d = await CommandAsync("CancelOpenAfterAssignment\t" + worktree))
            {
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
                AssertOpenedSnapshotPath(d.RootElement, original);
                Assert.Equal("rollback draft", d.RootElement.GetProperty("Draft").GetString());
                Assert.True(d.RootElement.GetProperty("CanChangeRepository").GetBoolean());
            }
            using (var d = await CommandAsync("SetDraft\t"))
                Assert.Equal(string.Empty, d.RootElement.GetProperty("Draft").GetString());

            using (var d = await CommandAsync("FailMutation"))
            {
                Assert.False(d.RootElement.GetProperty("Success").GetBoolean());
                Assert.Contains("Injected Git mutation failure", d.RootElement.GetProperty("Error").GetString());
                Assert.True(d.RootElement.GetProperty("CanChangeRepository").GetBoolean());
            }
            using (var d = await CommandAsync("CancelMutation"))
            {
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
                Assert.True(d.RootElement.GetProperty("CanChangeRepository").GetBoolean());
            }

            var beforeCommitCount = await GetCommitCountAsync(original, isolatedHome);
            using (var d = await CommandAsync("CommitWithFailedRefresh"))
            {
                Assert.False(d.RootElement.GetProperty("Success").GetBoolean());
                Assert.Contains("The Git operation completed", d.RootElement.GetProperty("Error").GetString());
                Assert.Contains("Injected post-commit refresh failure", d.RootElement.GetProperty("Error").GetString());
                Assert.True(d.RootElement.GetProperty("CanChangeRepository").GetBoolean());
                Assert.False(d.RootElement.GetProperty("IsBusy").GetBoolean());
            }
            Assert.Equal(beforeCommitCount + 1, await GetCommitCountAsync(original, isolatedHome));
            using (var d = await CommandAsync("Refresh"))
                AssertOpened(d.RootElement, original, false);
            Assert.Equal(beforeCommitCount + 1, await GetCommitCountAsync(original, isolatedHome));

            using (var d = await CommandAsync("ThrowCompletedHandler"))
            {
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
                Assert.False(d.RootElement.GetProperty("IsBusy").GetBoolean());
            }

            using (var d = await CommandAsync("BeginMutation"))
                Assert.True(d.RootElement.GetProperty("UnsafeToClose").GetBoolean());
            using (var d = await CommandAsync("AttemptCloseDuringMutation"))
                Assert.True(d.RootElement.GetProperty("UnsafeToClose").GetBoolean());
            using (var d = await CommandAsync("AttemptSwitchDuringMutation\t" + worktree))
            {
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
                AssertOpenedSnapshotPath(d.RootElement, original);
            }
            using (var d = await CommandAsync("AttemptCloseRepositoryDuringMutation"))
            {
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
                AssertOpenedSnapshotPath(d.RootElement, original);
            }
            using (var d = await CommandAsync("AttemptSecondMutation"))
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
            using (var d = await CommandAsync("CompleteMutation"))
            {
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
                Assert.False(d.RootElement.GetProperty("UnsafeToClose").GetBoolean());
            }

            using (var d = await CommandAsync("RetryMutation"))
            {
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
                Assert.False(d.RootElement.GetProperty("IsBusy").GetBoolean());
            }

            using (var d = await CommandAsync("CloseRepository"))
            {
                Assert.True(d.RootElement.GetProperty("Success").GetBoolean());
                Assert.Equal(JsonValueKind.Null, d.RootElement.GetProperty("Path").ValueKind);
                Assert.False(d.RootElement.GetProperty("HasBaseline").GetBoolean());
            }

            using (var d = await CommandAsync("Switch\t" + original))
                AssertOpened(d.RootElement, original, false);

            await writer.WriteLineAsync("Close".AsMemory(), timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync();
            }
        }

        var output = await stdout;
        Assert.Contains("DesktopCloseCheck: CloseCanceled: protected operation is active", output, StringComparison.Ordinal);
        Assert.Contains("DesktopCloseCheck: HostStopped", output, StringComparison.Ordinal);
        Assert.DoesNotContain("DesktopCloseCheck: Failed", output, StringComparison.Ordinal);
        Assert.True(process.ExitCode == 0, $"{process.ExitCode}: {output}{Environment.NewLine}{await stderr}");
    }

    private static async Task<int> GetCommitCountAsync(string repository, string isolatedHome)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(isolatedHome, ".gitconfig");
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["HOME"] = isolatedHome;
        start.ArgumentList.Add("rev-list");
        start.ArgumentList.Add("--count");
        start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Git commit count failed to start.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        return int.Parse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AssertOpenedSnapshotPath(JsonElement snapshot, string expected)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Assert.Equal(Path.GetFullPath(expected),
            Path.GetFullPath(snapshot.GetProperty("Path").GetString()!), comparison);
    }

    private static async Task WaitForCloseReadinessAsync(
        NamedPipeServerStream pipe, string repository, string applicationDataRoot, CancellationToken cancellationToken)
    {
        await pipe.WaitForConnectionAsync(cancellationToken);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        var message = await reader.ReadLineAsync(cancellationToken)
            ?? throw new InvalidOperationException("Desktop application disconnected before reporting readiness.");
        using var ready = JsonDocument.Parse(message);
        if (ready.RootElement.GetProperty("State").GetString() != "Ready")
            throw new InvalidOperationException($"Unexpected desktop readiness message: {message}");

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository)),
                Path.TrimEndingDirectorySeparator(ready.RootElement.GetProperty("Repository").GetString()!), comparison)
            || !string.Equals(Path.GetFullPath(applicationDataRoot),
                ready.RootElement.GetProperty("ApplicationDataRoot").GetString(), comparison))
            throw new InvalidOperationException($"Desktop application reported an unexpected repository or app-data root: {message}");
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
        start.Environment["CSHARPGIT_APP_DATA_ROOT"] = Path.Combine(isolatedHome, "appdata");
        foreach (var name in new[] { "CSHARPGIT_UI_CHECK_REPOSITORY", "CSHARPGIT_UI_CHECK_RESULT",
                     "CSHARPGIT_UI_CHECK_WORKTREE", "CSHARPGIT_CLOSE_CHECK", "CSHARPGIT_CLOSE_CHECK_PIPE", "CSHARPGIT_CLOSE_CHECK_EXTENDED" })
            start.Environment.Remove(name);
        return start;
    }

    private static string? FindApplication()
    {
        var root = FindRepositoryRoot();
        var name = OperatingSystem.IsWindows() ? "CSharpGit.exe" : "CSharpGit";
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
            await GitAsync(repository, home, "commit", "-m", $"Initial English commit {string.Join(' ', Enumerable.Repeat("long-message-segment", 100))}");
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
