using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class InteractiveRebaseAuthorChangeIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"csharpgit-author-rebase-{Guid.NewGuid():N}");
    private readonly string _a;
    private readonly string _b;
    private readonly string _c;
    private readonly string _d;
    private int _nextFile;

    public InteractiveRebaseAuthorChangeIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        Git("init", "-b", "main");
        Git("config", "user.name", "Original User");
        Git("config", "user.email", "original@example.com");

        _a = Commit("A", "2020-01-01T10:00:00+00:00");
        _b = Commit("B", "2020-01-02T10:00:00+00:00");
        _c = Commit("C", "2020-02-03T11:00:00+01:00");
        _d = Commit("D", "2020-03-04T12:00:00+02:00");
    }

    [Fact]
    public async Task RawRebaseChangesMultipleAuthorsAndPreservesAuthorDatesTreesAndMessages()
    {
        var before = new[] { _b, _c, _d }
            .Select(ReadCommit)
            .ToDictionary(commit => commit.Subject, StringComparer.Ordinal);
        var sentinel = Path.Combine(_root, "author-injection-sentinel");
        var (repository, workflow) = await CreateServicesAsync();
        var todo = await workflow.ReadInteractiveRebaseTodoFromCommitAsync(repository, _a);
        var transformer = new InteractiveRebaseAuthorChangeService();

        var transformed = transformer.Apply(
            new InteractiveRebaseAuthorChangeRequest(
                todo.TodoText,
                0,
                0,
                InteractiveRebaseAuthorChangeScope.AllEligibleCommits,
                "A 'quoted' $(touch author-injection-sentinel) $ & (test)",
                "o'connor@example.com",
                false));

        var result = await workflow.StartInteractiveRebaseTodoAsync(
            repository,
            todo with { TodoText = transformed.TodoText });

        Assert.Equal(RebaseResultKind.Completed, result.Kind);
        Assert.False(File.Exists(sentinel));

        var after = ReadRange(_a);
        Assert.Equal(["B", "C", "D"], after.Select(commit => commit.Subject).ToArray());

        foreach (var commit in after)
        {
            var original = before[commit.Subject];
            Assert.Equal(
                "A 'quoted' $(touch author-injection-sentinel) $ & (test)",
                commit.AuthorName);
            Assert.Equal("o'connor@example.com", commit.AuthorEmail);
            Assert.Equal(original.AuthorDate, commit.AuthorDate);
            Assert.Equal(original.Tree, commit.Tree);
            Assert.NotEqual(original.Hash, commit.Hash);
        }
    }

    [Fact]
    public async Task NativeResetAuthorUsesCurrentGitIdentityAndPreservesAuthorDates()
    {
        var before = new[] { _b, _c, _d }
            .Select(ReadCommit)
            .ToDictionary(commit => commit.Subject, StringComparer.Ordinal);
        Git("config", "user.name", "Reset User");
        Git("config", "user.email", "reset@example.com");

        var (repository, workflow) = await CreateServicesAsync();
        var todo = await workflow.ReadInteractiveRebaseTodoFromCommitAsync(repository, _a);
        var transformer = new InteractiveRebaseAuthorChangeService();

        var request = new InteractiveRebaseAuthorChangeRequest(
            todo.TodoText,
            0,
            0,
            InteractiveRebaseAuthorChangeScope.AllEligibleCommits,
            string.Empty,
            string.Empty,
            false,
            ResetToCurrentGitIdentity: true);
        var authorDates = await workflow.ReadCommitAuthorDatesAsync(
            repository,
            transformer.GetTargetCommits(request));
        var transformed = transformer.Apply(request, authorDates);

        Assert.Contains("--reset-author", transformed.TodoText, StringComparison.Ordinal);
        Assert.DoesNotContain("--author ", transformed.TodoText, StringComparison.Ordinal);
        Assert.DoesNotContain("reset@example.com", transformed.TodoText, StringComparison.Ordinal);
        Assert.DoesNotContain("$(", transformed.TodoText, StringComparison.Ordinal);
        Assert.DoesNotContain("git show", transformed.TodoText, StringComparison.Ordinal);

        var result = await workflow.StartInteractiveRebaseTodoAsync(
            repository,
            todo with { TodoText = transformed.TodoText });

        Assert.Equal(RebaseResultKind.Completed, result.Kind);

        var after = ReadRange(_a);
        Assert.Equal(["B", "C", "D"], after.Select(commit => commit.Subject).ToArray());
        Assert.Equal("refs/heads/main", GitOut("symbolic-ref", "HEAD"));

        foreach (var commit in after)
        {
            var original = before[commit.Subject];
            Assert.Equal("Reset User", commit.AuthorName);
            Assert.Equal("reset@example.com", commit.AuthorEmail);
            Assert.Equal(original.AuthorDate, commit.AuthorDate);
            Assert.Equal(original.Tree, commit.Tree);
            Assert.NotEqual(original.Hash, commit.Hash);
        }
    }

    [Fact]
    public async Task AuthorDatesAreReadInOneGitProcess()
    {
        var processCount = 0;
        var executor = GitTestServices.CreateExecutor(
            processStarted: _ => processCount++);
        var repository = await new GitRepositoryService(executor).OpenAsync(_root);
        var workflow = new GitRepositoryWorkflowService(executor);
        processCount = 0;

        var dates = await workflow.ReadCommitAuthorDatesAsync(
            repository,
            [_b, _c, _d]);

        Assert.Equal(1, processCount);
        Assert.Equal(ReadCommit(_b).AuthorDate, dates[_b]);
        Assert.Equal(ReadCommit(_c).AuthorDate, dates[_c]);
        Assert.Equal(ReadCommit(_d).AuthorDate, dates[_d]);
    }

    [Fact]
    public async Task AuthorDateReadFailsForMissingCommit()
    {
        var (repository, workflow) = await CreateServicesAsync();
        var missingCommit = new string('f', 40);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflow.ReadCommitAuthorDatesAsync(
                repository,
                [missingCommit]));

        Assert.Contains(
            "Could not read the original author date",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeResetAuthorCanRenewAuthorDate()
    {
        Git("config", "user.name", "Current User");
        Git("config", "user.email", "current@example.com");

        var (repository, workflow) = await CreateServicesAsync();
        var todo = await workflow.ReadInteractiveRebaseTodoFromCommitAsync(repository, _a);
        var transformer = new InteractiveRebaseAuthorChangeService();

        var transformed = transformer.Apply(
            new InteractiveRebaseAuthorChangeRequest(
                todo.TodoText,
                0,
                0,
                InteractiveRebaseAuthorChangeScope.AllEligibleCommits,
                string.Empty,
                string.Empty,
                true,
                ResetToCurrentGitIdentity: true));

        var result = await workflow.StartInteractiveRebaseTodoAsync(
            repository,
            todo with { TodoText = transformed.TodoText });

        Assert.Equal(RebaseResultKind.Completed, result.Kind);
        Assert.All(
            ReadRange(_a),
            commit =>
            {
                Assert.Equal("Current User", commit.AuthorName);
                Assert.Equal("current@example.com", commit.AuthorEmail);
                Assert.False(commit.AuthorDate.StartsWith("2020-", StringComparison.Ordinal));
            });
    }

    private string Commit(string subject, string date)
    {
        var fileName = $"commit-{++_nextFile}.txt";
        File.WriteAllText(Path.Combine(_root, fileName), subject + "\n");
        Git("add", "--", fileName);
        GitWithEnvironment(
            new Dictionary<string, string>
            {
                ["GIT_AUTHOR_DATE"] = date,
                ["GIT_COMMITTER_DATE"] = date
            },
            "commit",
            "-m",
            subject);
        return GitOut("rev-parse", "HEAD");
    }

    private CommitMetadata ReadCommit(string hash)
    {
        var fields = GitOut(
                "show",
                "-s",
                "--format=%H%x00%an%x00%ae%x00%aI%x00%T%x00%s",
                hash)
            .Split('\0');

        return new CommitMetadata(
            fields[0],
            fields[1],
            fields[2],
            fields[3],
            fields[4],
            fields[5]);
    }

    private IReadOnlyList<CommitMetadata> ReadRange(string onto) =>
        GitOut(
                "log",
                "--reverse",
                "--format=%H%x00%an%x00%ae%x00%aI%x00%T%x00%s",
                $"{onto}..HEAD")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var fields = line.Split('\0');
                return new CommitMetadata(
                    fields[0],
                    fields[1],
                    fields[2],
                    fields[3],
                    fields[4],
                    fields[5]);
            })
            .ToArray();

    private async Task<(Repository Repository, GitRepositoryWorkflowService Workflow)> CreateServicesAsync()
    {
        var executor = GitTestServices.CreateExecutor();
        var repository = await new GitRepositoryService(executor).OpenAsync(_root);
        return (repository, new GitRepositoryWorkflowService(executor));
    }

    private void Git(params string[] arguments)
    {
        var result = RunGit(arguments, null);
        Assert.True(
            result.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed: {result.Error}");
    }

    private void GitWithEnvironment(
        IReadOnlyDictionary<string, string> environment,
        params string[] arguments)
    {
        var result = RunGit(arguments, environment);
        Assert.True(
            result.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed: {result.Error}");
    }

    private string GitOut(params string[] arguments)
    {
        var result = RunGit(arguments, null);
        Assert.True(
            result.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed: {result.Error}");
        return result.Output.Trim();
    }

    private (int ExitCode, string Output, string Error) RunGit(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (environment is not null)
        {
            foreach (var pair in environment)
                startInfo.Environment[pair.Key] = pair.Value;
        }

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    public void Dispose() => TestDirectory.Delete(_root);

    private sealed record CommitMetadata(
        string Hash,
        string AuthorName,
        string AuthorEmail,
        string AuthorDate,
        string Tree,
        string Subject);
}
