using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class CommitTopologyLaneCountTests
{
    [Fact]
    public void TopologyProducerPersistsLaneCountCoveringEveryUsedLane()
    {
        var rows = GitReferenceHistoryService.BuildTopology(
        [
            Commit("M", "A", "B", "C", "D"),
            Commit("A", "A1"),
            Commit("B", "B1"),
            Commit("C", "C1"),
            Commit("D", "D1")
        ]);

        foreach (var row in rows)
        {
            var usedLanes = new[] { row.Topology.Lane }
                .Concat(row.Topology.IncomingEdges.SelectMany(edge => new[] { edge.FromLane, edge.ToLane }))
                .Concat(row.Topology.Edges.SelectMany(edge => new[] { edge.FromLane, edge.ToLane }));

            Assert.Equal(usedLanes.Max() + 1, row.Topology.LaneCount);
        }

        Assert.Equal(4, rows[0].Topology.LaneCount);
    }

    private static CommitHistoryItem Commit(string hash, params string[] parents) =>
        new(hash, parents, hash, hash, "tests", DateTimeOffset.UnixEpoch, []);
}
