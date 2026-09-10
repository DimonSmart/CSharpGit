using System.Diagnostics;

namespace CSharpGit.Git.Tests;

public sealed class GitReferenceHistoryMultipleMergeRegressionTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-multiple-merge-{Guid.NewGuid():N}");

    [Fact]
    public async Task MultipleSequentialMergesPreserveTrackContinuityBetweenRows()
    {
        InitializeRepository();

        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);
        var page = await new GitReferenceHistoryService().ReadHistoryAsync(repository, "main", null, 0, 100);

        var merges = page.Rows.Where(row => row.Commit.Parents.Count > 1).ToArray();
        Assert.Equal(new[] { "merge two", "merge one" }, merges.Select(row => row.Commit.Subject).ToArray());

        foreach (var merge in merges)
        {
            var parentEdges = merge.Topology.Edges
                .Where(edge => edge.FromLane == merge.Topology.Lane)
                .ToArray();
            Assert.Equal(merge.Commit.Parents.Count, parentEdges.Length);
            Assert.Equal(parentEdges.Length, parentEdges.Select(edge => edge.TrackId).Distinct().Count());
        }

        for (var index = 0; index + 1 < page.Rows.Count; index++)
        {
            var upper = page.Rows[index].Topology;
            var lower = page.Rows[index + 1].Topology;

            foreach (var edge in upper.Edges)
            {
                Assert.Contains(lower.IncomingEdges, incoming =>
                    incoming.FromLane == edge.ToLane
                    && incoming.ToLane == edge.ToLane
                    && incoming.TrackId == edge.TrackId);
            }
        }
    }

    private void InitializeRepository()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "tests@example.invalid");
        RunGit("config", "user.name", "CSharpGit Tests");

        WriteAndCommit("root.txt", "root\n", "root");

        RunGit("switch", "-c", "feature/one");
        WriteAndCommit("feature-one.txt", "feature one\n", "feature one");

        RunGit("switch", "main");
        WriteAndCommit("main-one.txt", "main one\n", "main one");
        RunGit("merge", "--no-ff", "feature/one", "-m", "merge one");

        RunGit("switch", "-c", "feature/two");
        WriteAndCommit("feature-two.txt", "feature two\n", "feature two");

        RunGit("switch", "main");
        WriteAndCommit("main-two.txt", "main two\n", "main two");
        RunGit("merge", "--no-ff", "feature/two", "-m", "merge two");
    }

    private void WriteAndCommit(string path, string content, string message)
    {
        File.WriteAllText(Path.Combine(_temporaryDirectory, path), content);
        RunGit("add", path);
        RunGit("commit", "-m", message);
    }

    private string RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _temporaryDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output.Trim();
    }

    public void Dispose() => TestDirectory.Delete(_temporaryDirectory);
}
