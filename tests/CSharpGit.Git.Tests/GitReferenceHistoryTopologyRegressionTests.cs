using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class GitReferenceHistoryTopologyRegressionTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-topology-{Guid.NewGuid():N}");

    [Fact]
    public void ResolvesParentLaneAfterAllParentInsertions()
    {
        var rows = GitReferenceHistoryService.BuildTopology(
        [
            Commit("M", "X", "L", "Q"),
            Commit("X", "Q", "N")
        ]);

        var first = rows[0];
        var qTrack = Assert.Single(first.Topology.Edges, edge => edge.FromLane == first.Topology.Lane && edge.ToLane == 2).TrackId;
        var shiftedParent = rows[1];

        Assert.Contains(shiftedParent.Topology.Edges, edge =>
            edge.FromLane == shiftedParent.Topology.Lane
            && edge.ToLane == 2
            && edge.TrackId == qTrack);
        Assert.DoesNotContain(shiftedParent.Topology.Edges, edge =>
            edge.FromLane == shiftedParent.Topology.Lane
            && edge.TrackId == qTrack
            && edge.ToLane != 2);
    }

    [Fact]
    public void PreservesTrackIdentityAcrossThreeParallelLanes()
    {
        var rows = GitReferenceHistoryService.BuildTopology(
        [
            Commit("M", "A", "B", "C"),
            Commit("A", "A1"),
            Commit("B", "B1"),
            Commit("C", "C1")
        ]);

        var branchTracks = rows[0].Topology.Edges
            .Where(edge => edge.FromLane == rows[0].Topology.Lane)
            .OrderBy(edge => edge.ToLane)
            .Select(edge => edge.TrackId)
            .ToArray();
        Assert.Equal(3, branchTracks.Distinct().Count());
        Assert.Equal(branchTracks[0], rows[1].Topology.NodeTrackId);
        Assert.Equal(branchTracks[1], rows[2].Topology.NodeTrackId);
        Assert.Equal(branchTracks[2], rows[3].Topology.NodeTrackId);
    }

    [Fact]
    public void BuildsAllEdgesForOctopusMerge()
    {
        var row = Assert.Single(GitReferenceHistoryService.BuildTopology(
        [
            Commit("M", "A", "B", "C", "D")
        ]));

        var parentEdges = row.Topology.Edges.Where(edge => edge.FromLane == row.Topology.Lane).ToArray();
        Assert.Equal(4, parentEdges.Length);
        Assert.Equal(new[] { 0, 1, 2, 3 }, parentEdges.Select(edge => edge.ToLane).Order().ToArray());
        Assert.Equal(4, parentEdges.Select(edge => edge.TrackId).Distinct().Count());
    }

    [Fact]
    public async Task FilteredRowsUseTopologyFromCompleteHistory()
    {
        InitializeMergeRepository();
        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);
        var service = new GitReferenceHistoryService();
        var full = await service.ReadHistoryAsync(repository, "main", null, 0, 100);
        var filtered = await service.ReadHistoryAsync(repository, "main", "visible", 0, 100);

        Assert.Equal(new[] { "visible tip", "visible root" }, filtered.Rows.Select(row => row.Commit.Subject).ToArray());
        Assert.All(filtered.Rows, row => Assert.True(row.Topology.HasExactGraphTopology));
        foreach (var actual in filtered.Rows)
        {
            var expected = Assert.Single(full.Rows, row => row.Commit.Hash == actual.Commit.Hash);
            AssertTopologyEqual(expected.Topology, actual.Topology);
        }
    }

    [Fact]
    public async Task PaginationContinuesTheSameTopologyAcrossPageBoundary()
    {
        InitializeMergeRepository();
        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);
        var service = new GitReferenceHistoryService();
        var full = await service.ReadHistoryAsync(repository, "main", null, 0, 100);
        var page1 = await service.ReadHistoryAsync(repository, "main", null, 0, 2);
        var page2 = await service.ReadHistoryAsync(repository, "main", null, 2, 2);
        var combined = page1.Rows.Concat(page2.Rows).ToArray();

        Assert.Equal(full.Rows.Take(4).Select(row => row.Commit.Hash), combined.Select(row => row.Commit.Hash));
        for (var index = 0; index < combined.Length; index++)
            AssertTopologyEqual(full.Rows[index].Topology, combined[index].Topology);
    }

    [Fact]
    public async Task FilterPaginationStillUsesCompleteTopology()
    {
        InitializeMergeRepository();
        var repository = await new GitCliRepositoryService().OpenAsync(_temporaryDirectory);
        var service = new GitReferenceHistoryService();
        var full = await service.ReadHistoryAsync(repository, "main", null, 0, 100);
        var page1 = await service.ReadHistoryAsync(repository, "main", "visible", 0, 1);
        var page2 = await service.ReadHistoryAsync(repository, "main", "visible", 1, 1);
        var combined = page1.Rows.Concat(page2.Rows).ToArray();

        Assert.True(page1.HasMore);
        Assert.False(page2.HasMore);
        Assert.Equal(new[] { "visible tip", "visible root" }, combined.Select(row => row.Commit.Subject).ToArray());
        foreach (var actual in combined)
        {
            var expected = Assert.Single(full.Rows, row => row.Commit.Hash == actual.Commit.Hash);
            AssertTopologyEqual(expected.Topology, actual.Topology);
        }
    }

    private static CommitHistoryItem Commit(string hash, params string[] parents) =>
        new(hash, parents, hash, hash, "tests", DateTimeOffset.UnixEpoch, []);

    private static void AssertTopologyEqual(CommitTopology expected, CommitTopology actual)
    {
        Assert.Equal(expected.Lane, actual.Lane);
        Assert.Equal(expected.NodeTrackId, actual.NodeTrackId);
        Assert.Equal(expected.HasExactGraphTopology, actual.HasExactGraphTopology);
        Assert.Equal(expected.IncomingEdges.ToArray(), actual.IncomingEdges.ToArray());
        Assert.Equal(expected.Edges.ToArray(), actual.Edges.ToArray());
    }

    private void InitializeMergeRepository()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "tests@example.invalid");
        RunGit("config", "user.name", "CSharpGit Tests");

        File.WriteAllText(Path.Combine(_temporaryDirectory, "root.txt"), "root\n");
        RunGit("add", "root.txt");
        RunGit("commit", "-m", "visible root");

        RunGit("switch", "-c", "feature/test");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "feature.txt"), "feature\n");
        RunGit("add", "feature.txt");
        RunGit("commit", "-m", "hidden feature");

        RunGit("switch", "main");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "main.txt"), "main\n");
        RunGit("add", "main.txt");
        RunGit("commit", "-m", "hidden main");
        RunGit("merge", "--no-ff", "feature/test", "-m", "hidden merge");

        File.WriteAllText(Path.Combine(_temporaryDirectory, "tip.txt"), "tip\n");
        RunGit("add", "tip.txt");
        RunGit("commit", "-m", "visible tip");
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
