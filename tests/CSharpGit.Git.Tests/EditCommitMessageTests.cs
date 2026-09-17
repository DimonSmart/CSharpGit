using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class EditCommitMessageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"csharpgit-edit-message-{Guid.NewGuid():N}");

    [Fact]
    public async Task HeadEditPreservesTreeParentAndAuthorAndSupportsMultilineMessage()
    {
        Init();
        Commit("base.txt", "base\n", "base");
        var oldHead = Commit("tracked.txt", "value\n", "old message");
        var oldTree = Git("show", "-s", "--format=%T", oldHead);
        var oldParent = Git("show", "-s", "--format=%P", oldHead);
        var oldAuthor = Git("show", "-s", "--format=%an%x00%ae%x00%aI", oldHead);

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        const string newMessage = "new subject\n\nmultiline body";

        var result = await service.EditCommitMessageAsync(repository, oldHead, newMessage);

        Assert.Equal(EditCommitMessageResultKind.Completed, result.Kind);
        Assert.NotNull(result.NewCommit);
        Assert.Equal(result.NewCommit, result.NewHead);
        Assert.NotEqual(oldHead, result.NewCommit);
        Assert.Equal(oldTree, Git("show", "-s", "--format=%T", result.NewCommit!));
        Assert.Equal(oldParent, Git("show", "-s", "--format=%P", result.NewCommit!));
        Assert.Equal(oldAuthor, Git("show", "-s", "--format=%an%x00%ae%x00%aI", result.NewCommit!));
        Assert.Equal(newMessage, Git("show", "-s", "--format=%B", result.NewCommit!));
    }

    [Theory]
    [InlineData("staged")]
    [InlineData("unstaged")]
    [InlineData("untracked")]
    public async Task DirtyWorkingTreeBlocksHeadEdit(string dirtyKind)
    {
        Init();
        var oldHead = Commit("tracked.txt", "base\n", "old");

        if (dirtyKind == "untracked")
        {
            File.WriteAllText(Path.Combine(_root, "untracked.txt"), "new\n");
        }
        else
        {
            File.WriteAllText(Path.Combine(_root, "tracked.txt"), "dirty\n");
            if (dirtyKind == "staged") Run("add", "tracked.txt");
        }

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);

        var result = await service.EditCommitMessageAsync(repository, oldHead, "new");

        Assert.Equal(EditCommitMessageResultKind.Failed, result.Kind);
        Assert.Contains("working tree contains changes", result.Message, StringComparison.Ordinal);
        Assert.Equal(oldHead, Git("rev-parse", "HEAD"));
        Assert.Equal("old", Git("show", "-s", "--format=%s", "HEAD"));
    }

    [Fact]
    public async Task HistoricalEditRewritesOnlyCurrentLinearHistoryAndReturnsDeterministicTarget()
    {
        Init();
        Commit("tracked.txt", "base\n", "base");
        var target = Commit("tracked.txt", "target\n", "duplicate");
        var targetTree = Git("show", "-s", "--format=%T", target);
        Run("branch", "also-here", target);
        Commit("later.txt", "later\n", "duplicate");
        var oldHead = Commit("tip.txt", "tip\n", "tip");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        const string newMessage = "updated target\n\nbody";

        var result = await service.EditCommitMessageAsync(repository, target, newMessage);

        Assert.Equal(EditCommitMessageResultKind.Completed, result.Kind);
        Assert.NotNull(result.NewCommit);
        Assert.NotNull(result.NewHead);
        Assert.NotEqual(target, result.NewCommit);
        Assert.NotEqual(oldHead, result.NewHead);
        Assert.Equal(targetTree, Git("show", "-s", "--format=%T", result.NewCommit!));
        Assert.Equal(newMessage, Git("show", "-s", "--format=%B", result.NewCommit!));
        Assert.Equal("duplicate", Git("show", "-s", "--format=%s", $"{result.NewHead}~1"));
        Assert.Equal("tip", Git("show", "-s", "--format=%s", result.NewHead!));
        Assert.Equal(target, Git("rev-parse", "also-here"));
        Assert.Equal(result.NewCommit, Git("rev-parse", $"{result.NewHead}~2"));
        Assert.False(Directory.Exists(Path.Combine(repository.GitDirectory, "csharpgit-rebase")));
    }

    [Fact]
    public async Task HistoricalEditRejectsCommitOutsideCurrentHistoryAndDetachedHead()
    {
        Init();
        var root = Commit("tracked.txt", "base\n", "base");
        Run("switch", "-c", "side");
        var outside = Commit("side.txt", "side\n", "side");
        Run("switch", "main");
        Commit("main.txt", "main\n", "main");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);

        var outsideResult = await service.EditCommitMessageAsync(repository, outside, "changed");
        Assert.Equal(EditCommitMessageResultKind.Failed, outsideResult.Kind);
        Assert.Contains("not part of the current branch history", outsideResult.Message, StringComparison.Ordinal);

        var head = Git("rev-parse", "HEAD");
        await service.CheckoutAsync(repository, head);
        var detachedResult = await service.EditCommitMessageAsync(repository, head, "changed");
        Assert.Equal(EditCommitMessageResultKind.Failed, detachedResult.Kind);
        Assert.Contains("attached to a local branch", detachedResult.Message, StringComparison.Ordinal);

        Assert.NotEqual(root, outside);
    }

    [Fact]
    public async Task HistoricalEditRejectsRootAndMergeTopology()
    {
        Init();
        var root = Commit("tracked.txt", "base\n", "base");
        var target = Commit("tracked.txt", "target\n", "target");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);

        var rootResult = await service.EditCommitMessageAsync(repository, root, "root changed");
        Assert.Equal(EditCommitMessageResultKind.Failed, rootResult.Kind);
        Assert.Contains("root commit", rootResult.Message, StringComparison.Ordinal);

        Run("switch", "-c", "feature");
        Commit("feature.txt", "feature\n", "feature");
        Run("switch", "main");
        Commit("main.txt", "main\n", "main");
        Run("merge", "--no-ff", "feature", "-m", "merge");
        var merge = Git("rev-parse", "HEAD");
        Commit("tip.txt", "tip\n", "tip");

        var rangeResult = await service.EditCommitMessageAsync(repository, target, "target changed");
        Assert.Equal(EditCommitMessageResultKind.Failed, rangeResult.Kind);
        Assert.Contains("contains merges", rangeResult.Message, StringComparison.Ordinal);

        var mergeResult = await service.EditCommitMessageAsync(repository, merge, "merge changed");
        Assert.Equal(EditCommitMessageResultKind.Failed, mergeResult.Kind);
        Assert.Contains("merge commit", mergeResult.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActiveGitOperationBlocksEdit()
    {
        Init();
        Commit("tracked.txt", "base\n", "base");
        Run("switch", "-c", "feature");
        var conflictCommit = Commit("tracked.txt", "feature\n", "feature");
        Run("switch", "main");
        var mainHead = Commit("tracked.txt", "main\n", "main");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var conflict = await service.CherryPickAsync(repository, conflictCommit);
        Assert.Equal(ApplyCommitResultKind.Conflicts, conflict.Kind);

        var result = await service.EditCommitMessageAsync(repository, mainHead, "changed");

        Assert.Equal(EditCommitMessageResultKind.Failed, result.Kind);
        Assert.Contains("current Git operation", result.Message, StringComparison.Ordinal);
        await service.AbortOperationAsync(repository);
    }

    [Fact]
    public async Task DirtyWorkingTreeBlocksHistoricalEdit()
    {
        Init();
        Commit("tracked.txt", "base\n", "base");
        var target = Commit("tracked.txt", "target\n", "target");
        Commit("tip.txt", "tip\n", "tip");
        File.WriteAllText(Path.Combine(_root, "untracked.txt"), "dirty\n");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);

        var result = await service.EditCommitMessageAsync(repository, target, "changed");

        Assert.Equal(EditCommitMessageResultKind.Failed, result.Kind);
        Assert.Contains("working tree contains changes", result.Message, StringComparison.Ordinal);
    }

    private void Init()
    {
        Directory.CreateDirectory(_root);
        Run("init", "-b", "main");
        Run("config", "user.email", "tests@example.invalid");
        Run("config", "user.name", "CSharpGit Tests");
        Run("config", "core.editor", "csharpgit-editor-must-not-run");
        Run("config", "commit.gpgSign", "false");
    }

    private string Commit(string path, string contents, string message)
    {
        File.WriteAllText(Path.Combine(_root, path), contents);
        Run("add", "--", path);
        Run("commit", "-m", message);
        return Git("rev-parse", "HEAD");
    }

    private void Run(params string[] arguments)
    {
        var result = RunCore(arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {result.Error}");
    }

    private string Git(params string[] arguments)
    {
        var result = RunCore(arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {result.Error}");
        return result.Output.Trim();
    }

    private (int ExitCode, string Output, string Error) RunCore(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    public void Dispose() => TestDirectory.Delete(_root);
}
