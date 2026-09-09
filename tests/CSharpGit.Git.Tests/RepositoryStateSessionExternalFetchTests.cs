using System.Diagnostics;
using CSharpGit.Application;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class RepositoryStateSessionExternalFetchTests : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(Path.GetTempPath(), $"csharpgit-external-fetch-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExternalFetchPublishesRemoteRefChangeThroughPolling()
    {
        var observedDirectory = Path.Combine(_temporaryDirectory, "observed");
        var producerDirectory = Path.Combine(_temporaryDirectory, "producer");
        var remoteDirectory = Path.Combine(_temporaryDirectory, "origin.git");
        Directory.CreateDirectory(observedDirectory);

        RunGit(observedDirectory, "init", "-b", "main");
        ConfigureIdentity(observedDirectory);
        File.WriteAllText(Path.Combine(observedDirectory, "tracked.txt"), "A\n");
        RunGit(observedDirectory, "add", "tracked.txt");
        RunGit(observedDirectory, "commit", "-m", "A");
        var commitA = RunGitOutput(observedDirectory, "rev-parse", "HEAD").Trim();

        RunGit(_temporaryDirectory, "init", "--bare", remoteDirectory);
        RunGit(observedDirectory, "remote", "add", "origin", remoteDirectory);
        RunGit(observedDirectory, "push", "-u", "origin", "main");
        RunGit(observedDirectory, "fetch", "origin");

        RunGit(_temporaryDirectory, "clone", observedDirectory, producerDirectory);
        ConfigureIdentity(producerDirectory);
        RunGit(producerDirectory, "remote", "set-url", "origin", remoteDirectory);
        File.WriteAllText(Path.Combine(producerDirectory, "tracked.txt"), "B\n");
        RunGit(producerDirectory, "commit", "-am", "B");
        var commitB = RunGitOutput(producerDirectory, "rev-parse", "HEAD").Trim();
        RunGit(producerDirectory, "push", "origin", "main");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(observedDirectory);
        await using var session = await new RepositoryStateSessionFactory(service).CreateAsync(repository);
        Assert.Equal(commitA, session.Current.HeadCommit);
        Assert.Empty(session.Current.Changes);
        Assert.Equal(
            commitA,
            Assert.Single(session.Current.Refs.RemoteBranches, branch => branch.Name == "origin/main").Commit);

        var changed = new TaskCompletionSource<RepositoryState>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += (_, state) =>
        {
            if (state.Refs.RemoteBranches.Any(branch =>
                    branch.Name == "origin/main" &&
                    StringComparer.Ordinal.Equals(branch.Commit, commitB)))
            {
                changed.TrySetResult(state);
            }
        };

        RunGit(observedDirectory, "fetch", "origin");

        var observed = await changed.Task.WaitAsync(TimeSpan.FromSeconds(7));

        Assert.Equal(commitA, observed.HeadCommit);
        Assert.Equal(commitA, session.Current.HeadCommit);
        Assert.Empty(observed.Changes);
        Assert.Equal(
            commitB,
            Assert.Single(observed.Refs.RemoteBranches, branch => branch.Name == "origin/main").Commit);
        Assert.Equal(
            commitB,
            Assert.Single(session.Current.Refs.RemoteBranches, branch => branch.Name == "origin/main").Commit);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_temporaryDirectory))
        {
            return;
        }

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(_temporaryDirectory, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_temporaryDirectory, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(100 * attempt);
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(100 * attempt);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    private static void ConfigureIdentity(string directory)
    {
        RunGit(directory, "config", "user.email", "tests@example.invalid");
        RunGit(directory, "config", "user.name", "CSharpGit Tests");
    }

    private static void RunGit(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(" ", arguments)} failed: {standardError}");
    }

    private static string RunGitOutput(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(" ", arguments)} failed: {standardError}");
        return output;
    }
}
