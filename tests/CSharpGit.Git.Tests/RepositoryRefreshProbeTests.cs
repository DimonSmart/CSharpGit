using System.Diagnostics;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Git.Tests;

public sealed class RepositoryRefreshProbeTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"csharpgit-refresh-probe-{Guid.NewGuid():N}");

    [Fact]
    public async Task IgnoredActivityDoesNotChangeFingerprint()
    {
        var (_, repository, probe) = await CreateRepositoryAsync();
        var before = await probe.ReadAsync(repository);

        Directory.CreateDirectory(Path.Combine(_root, "bin"));
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        Directory.CreateDirectory(Path.Combine(_root, ".local-vector-search-mcp"));
        await File.WriteAllTextAsync(Path.Combine(_root, "bin", "a.dll"), "ignored");
        await File.WriteAllTextAsync(Path.Combine(_root, "obj", "cache"), "ignored");
        await File.WriteAllTextAsync(Path.Combine(_root, ".local-vector-search-mcp", "index.db"), "ignored");

        Assert.Equal(before, await probe.ReadAsync(repository));
    }

    [Fact]
    public async Task RepeatedTrackedEditChangesFingerprintEvenWhenStatusStaysModified()
    {
        var (_, repository, probe) = await CreateRepositoryAsync();
        var path = Path.Combine(_root, "tracked.txt");

        await File.AppendAllTextAsync(path, "one\n");
        var first = await probe.ReadAsync(repository);
        await File.AppendAllTextAsync(path, "two\n");

        Assert.NotEqual(first, await probe.ReadAsync(repository));
    }

    [Fact]
    public async Task RepeatedUntrackedEditChangesFingerprint()
    {
        var (_, repository, probe) = await CreateRepositoryAsync();
        var path = Path.Combine(_root, "visible.tmp");

        await File.WriteAllTextAsync(path, "one");
        var first = await probe.ReadAsync(repository);
        await File.WriteAllTextAsync(path, "two");

        Assert.NotEqual(first, await probe.ReadAsync(repository));
    }

    [Fact]
    public async Task RepeatedStageChangesIndexFingerprint()
    {
        var (_, repository, probe) = await CreateRepositoryAsync();
        var path = Path.Combine(_root, "tracked.txt");

        await File.WriteAllTextAsync(path, "staged-one\n");
        RunGit(_root, "add", "tracked.txt");
        var first = await probe.ReadAsync(repository);

        await File.WriteAllTextAsync(path, "staged-two\n");
        RunGit(_root, "add", "tracked.txt");

        Assert.NotEqual(first, await probe.ReadAsync(repository));
    }

    [Fact]
    public async Task ProbeDoesNotRewriteIndex()
    {
        var (_, repository, probe) = await CreateRepositoryAsync();
        var indexPath = Path.Combine(repository.GitDirectory, "index");
        var timestamp = DateTime.UtcNow.AddHours(-2);
        File.SetLastWriteTimeUtc(indexPath, timestamp);
        var before = await File.ReadAllBytesAsync(indexPath);
        var beforeWrite = File.GetLastWriteTimeUtc(indexPath);

        _ = await probe.ReadAsync(repository);

        Assert.Equal(before, await File.ReadAllBytesAsync(indexPath));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(indexPath));
    }

    [Fact]
    public async Task ProbeIsLocalOnlyWithUnreachableRemote()
    {
        var (_, repository, probe) = await CreateRepositoryAsync();
        RunGit(_root, "remote", "add", "offline", "https://example.invalid/csharpgit.git");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _ = await probe.ReadAsync(repository, timeout.Token);
    }

    private async Task<(GitRepositoryService Service, CSharpGit.Domain.Repository Repository, IRepositoryRefreshProbe Probe)> CreateRepositoryAsync()
    {
        Directory.CreateDirectory(_root);
        RunGit(_root, "init", "-b", "main");
        RunGit(_root, "config", "user.email", "tests@example.invalid");
        RunGit(_root, "config", "user.name", "Refresh Probe Tests");
        await File.WriteAllTextAsync(
            Path.Combine(_root, ".gitignore"),
            "bin/\nobj/\n.vs/\n.idea/\n.local-vector-search-mcp/\n");
        await File.WriteAllTextAsync(Path.Combine(_root, "tracked.txt"), "initial\n");
        RunGit(_root, "add", ".gitignore", "tracked.txt");
        RunGit(_root, "commit", "-m", "Initial");

        var executor = GitTestServices.CreateExecutor();
        var service = new GitRepositoryService(executor);
        var repository = await service.OpenAsync(_root);
        IRepositoryRefreshProbe probe = new GitRepositoryRefreshProbe(executor);
        return (service, repository, probe);
    }

    public void Dispose() => TestDirectory.Delete(_root);

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git failed: {stderr}{stdout}");
    }
}
