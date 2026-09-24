using System.Diagnostics;

namespace CSharpGit.Git.Tests;

public sealed class GitReferenceHistoryServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"csharpgit-ref-history-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("AuthorName", "author@example.com")]
    [InlineData("Дмитрий Тест", "12345+DimonSmart@users.noreply.github.com")]
    [InlineData("Unicode Ω", "not-an-email")]
    public async Task ReadsAuthorEmailWithExistingCommitMetadata(string authorName, string authorEmail)
    {
        InitializeRepository();
        RunGit("config", "user.name", authorName);
        RunGit("config", "user.email", authorEmail);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "file.txt"), "content\n");
        RunGit("add", "file.txt");
        RunGit("commit", "-m", "authored commit");
        var hash = RunGit("rev-parse", "HEAD");

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(_temporaryDirectory);
        var service = GitTestServices.CreateReferenceHistoryService();

        var page = await service.ReadHistoryAsync(repository, "main", null, 0, 20);
        var details = await service.ReadCommitAsync(repository, hash);

        var historyCommit = Assert.Single(page.Rows).Commit;
        Assert.Equal(authorName, historyCommit.Author);
        Assert.Equal(authorEmail, historyCommit.AuthorEmail);
        Assert.Equal(authorName, details.Commit.Author);
        Assert.Equal(authorEmail, details.Commit.AuthorEmail);
    }

    [Fact]
    public async Task ReadsSelectedBranchWithoutCheckingItOut()
    {
        InitializeRepository();
        File.WriteAllText(Path.Combine(_temporaryDirectory, "file.txt"), "initial\n");
        RunGit("add", "file.txt");
        RunGit("commit", "-m", "initial");
        RunGit("switch", "-c", "feature/demo");
        File.AppendAllText(Path.Combine(_temporaryDirectory, "file.txt"), "feature\n");
        RunGit("commit", "-am", "feature commit");
        RunGit("switch", "main");
        File.AppendAllText(Path.Combine(_temporaryDirectory, "file.txt"), "main\n");
        RunGit("commit", "-am", "main commit");

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(_temporaryDirectory);
        var page = await GitTestServices.CreateReferenceHistoryService().ReadHistoryAsync(repository, "feature/demo", null, 0, 20);

        Assert.Equal("feature commit", page.Rows[0].Commit.Subject);
        Assert.Contains(page.Rows, row => row.Commit.Subject == "initial");
        Assert.DoesNotContain(page.Rows, row => row.Commit.Subject == "main commit");
        Assert.Equal("main", RunGit("branch", "--show-current"));
    }

    [Fact]
    public async Task BranchTipStartsAtNodeWithoutIncomingEdge()
    {
        InitializeRepository();
        File.WriteAllText(Path.Combine(_temporaryDirectory, "file.txt"), "initial\n");
        RunGit("add", "file.txt");
        RunGit("commit", "-m", "initial");
        File.AppendAllText(Path.Combine(_temporaryDirectory, "file.txt"), "latest\n");
        RunGit("commit", "-am", "latest");

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(_temporaryDirectory);
        var page = await GitTestServices.CreateReferenceHistoryService().ReadHistoryAsync(repository, "main", null, 0, 20);

        var tip = page.Rows[0];
        Assert.Equal("latest", tip.Commit.Subject);
        Assert.True(tip.Topology.HasExactGraphTopology);
        Assert.Empty(tip.Topology.IncomingEdges);
        Assert.Contains(tip.Topology.Edges, edge =>
            edge.FromLane == tip.Topology.Lane
            && edge.TrackId == tip.Topology.NodeTrackId);
    }

    [Fact]
    public async Task PreservesContinuousTracksAcrossMergeRows()
    {
        InitializeRepository();
        File.WriteAllText(Path.Combine(_temporaryDirectory, "file.txt"), "initial\n");
        RunGit("add", "file.txt");
        RunGit("commit", "-m", "initial");

        RunGit("switch", "-c", "feature/demo");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "feature.txt"), "feature\n");
        RunGit("add", "feature.txt");
        RunGit("commit", "-m", "feature commit");

        RunGit("switch", "main");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "main.txt"), "main\n");
        RunGit("add", "main.txt");
        RunGit("commit", "-m", "main commit");
        RunGit("merge", "--no-ff", "feature/demo", "-m", "merge feature");

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(_temporaryDirectory);
        var page = await GitTestServices.CreateReferenceHistoryService().ReadHistoryAsync(repository, "main", null, 0, 20);

        var merge = Assert.Single(page.Rows, row => row.Commit.Subject == "merge feature");
        var parentEdges = merge.Topology.Edges.Where(edge => edge.FromLane == merge.Topology.Lane).ToList();
        Assert.Equal(2, parentEdges.Count);
        Assert.Equal(2, parentEdges.Select(edge => edge.TrackId).Distinct().Count());

        var mergeIndex = page.Rows.ToList().IndexOf(merge);
        Assert.True(mergeIndex >= 0 && mergeIndex + 1 < page.Rows.Count);
        var rowAfterMerge = page.Rows[mergeIndex + 1];
        Assert.Contains(rowAfterMerge.Topology.IncomingEdges, edge => edge.TrackId == parentEdges[0].TrackId);
        Assert.Contains(rowAfterMerge.Topology.IncomingEdges, edge => edge.TrackId == parentEdges[1].TrackId);
    }

    [Fact]
    public async Task ReadsCompactFileStatusForCommit()
    {
        InitializeRepository();
        File.WriteAllText(Path.Combine(_temporaryDirectory, "file.txt"), "initial\n");
        RunGit("add", "file.txt");
        RunGit("commit", "-m", "initial");
        File.AppendAllText(Path.Combine(_temporaryDirectory, "file.txt"), "changed\n");
        RunGit("commit", "-am", "change");
        var hash = RunGit("rev-parse", "HEAD");

        var repository = await GitTestServices.CreateRepositoryService().OpenAsync(_temporaryDirectory);
        var statuses = await GitTestServices.CreateReferenceHistoryService().ReadFileStatusesAsync(repository, hash);

        Assert.Equal("M", statuses["file.txt"]);
    }

    private void InitializeRepository()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "tests@example.invalid");
        RunGit("config", "user.name", "CSharpGit Tests");
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
