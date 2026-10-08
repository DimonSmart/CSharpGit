using System.Diagnostics;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

[Collection(GitToolsEnvironmentCollection.CollectionName)]
public sealed class GitConfigServiceTests : IClassFixture<GitToolsEmptyFixture>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-git-config-{Guid.NewGuid():N}");
    private readonly string _repositoryPath;
    private readonly string _globalConfig;
    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);
    private readonly Repository _repository;
    private readonly GitConfigService _service;

    public GitConfigServiceTests(GitToolsEmptyFixture history)
    {
        _repositoryPath = history.CreateCopy();
        _globalConfig = Path.Combine(_root, "global.gitconfig");
        Directory.CreateDirectory(_root);
        File.WriteAllText(_globalConfig, string.Empty);

        SetEnvironment("GIT_CONFIG_GLOBAL", _globalConfig);
        SetEnvironment("GIT_CONFIG_NOSYSTEM", "1");
        SetEnvironment("GIT_CONFIG_SYSTEM", null);
        SetEnvironment("GIT_CONFIG_COUNT", null);
        SetEnvironment("GIT_CONFIG_KEY_0", null);
        SetEnvironment("GIT_CONFIG_VALUE_0", null);

        _repository = new Repository(
            Path.GetFullPath(_repositoryPath),
            Path.GetFullPath(_repositoryPath),
            Path.GetFullPath(Path.Combine(_repositoryPath, ".git")),
            false);
        _service = new GitConfigService(GitTestServices.CreateExecutor());
    }

    [Fact]
    public async Task EffectiveGlobalReportsGlobalSource()
    {
        RunGit(_repositoryPath, "config", "--global", "user.name", "Global User");

        var value = await _service.ReadEffectiveAsync(_repository, "user.name");

        Assert.NotNull(value);
        Assert.Equal("Global User", value.Value);
        Assert.Equal(GitConfigSource.Global, value.Source);
        Assert.Contains("global.gitconfig", value.Origin ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectLocalIsDistinctFromEffectiveConfiguration()
    {
        RunGit(_repositoryPath, "config", "--global", "user.name", "Global User");
        RunGit(_repositoryPath, "config", "--local", "user.name", "Local User");

        var effective = await _service.ReadEffectiveAsync(_repository, "user.name");
        var direct = await _service.ReadDirectScopeAsync(_repository, "user.name", GitConfigScope.Repository);

        Assert.Equal("Local User", effective?.Value);
        Assert.Equal(GitConfigSource.Repository, effective?.Source);
        Assert.Equal("Local User", direct?.Value);
    }

    [Fact]
    public async Task IncludedGlobalKeepsGlobalScopeAndIncludedOrigin()
    {
        var included = Path.Combine(_root, "company-global.gitconfig");
        File.WriteAllText(included, "[user]\n\tname = Included Global\n");
        RunGit(_repositoryPath, "config", "--global", "include.path", included);

        var effective = await _service.ReadEffectiveAsync(_repository, "user.name");

        Assert.Equal("Included Global", effective?.Value);
        Assert.Equal(GitConfigSource.Global, effective?.Source);
        Assert.Contains("company-global.gitconfig", effective?.Origin ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IncludedLocalIsEffectiveRepositoryButNotDirectOverride()
    {
        var included = Path.Combine(_root, "company-local.gitconfig");
        File.WriteAllText(included, "[user]\n\tname = Included Local\n");
        RunGit(_repositoryPath, "config", "--local", "include.path", included);

        var effective = await _service.ReadEffectiveAsync(_repository, "user.name");
        var direct = await _service.ReadDirectScopeAsync(_repository, "user.name", GitConfigScope.Repository);

        Assert.Equal("Included Local", effective?.Value);
        Assert.Equal(GitConfigSource.Repository, effective?.Source);
        Assert.Contains("company-local.gitconfig", effective?.Origin ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Null(direct);
    }

    [Fact]
    public async Task WorktreeScopeWinsWhileDirectLocalRemainsVisible()
    {
        RunGit(_repositoryPath, "config", "extensions.worktreeConfig", "true");
        RunGit(_repositoryPath, "config", "--local", "user.name", "Local User");
        RunGit(_repositoryPath, "config", "--worktree", "user.name", "Worktree User");

        var effective = await _service.ReadEffectiveAsync(_repository, "user.name");
        var direct = await _service.ReadDirectScopeAsync(_repository, "user.name", GitConfigScope.Repository);
        var worktree = await _service.ReadScopeAsync(_repository, "user.name", GitConfigScope.Worktree);

        Assert.Equal("Worktree User", effective?.Value);
        Assert.Equal(GitConfigSource.Worktree, effective?.Source);
        Assert.Equal("Local User", direct?.Value);
        Assert.Equal("Worktree User", worktree?.Value);
    }

    [Fact]
    public async Task CommandConfigurationIsReportedAsCommandSource()
    {
        RunGit(_repositoryPath, "config", "--local", "user.name", "Local User");
        SetEnvironment("GIT_CONFIG_COUNT", "1");
        SetEnvironment("GIT_CONFIG_KEY_0", "user.name");
        SetEnvironment("GIT_CONFIG_VALUE_0", "Command User");

        var effective = await _service.ReadEffectiveAsync(_repository, "user.name");

        Assert.Equal("Command User", effective?.Value);
        Assert.Equal(GitConfigSource.Command, effective?.Source);
    }

    [Fact]
    public async Task ReplaceAllNormalizesDuplicateDirectValuesAndUnsetAllRemovesThem()
    {
        RunGit(_repositoryPath, "config", "--local", "--add", "user.name", "First");
        RunGit(_repositoryPath, "config", "--local", "--add", "user.name", "Second");
        Assert.Equal(2, (await _service.ReadDirectValuesAsync(_repository, "user.name", GitConfigScope.Repository)).Count);

        await _service.SetValueAsync(_repository, GitConfigScope.Repository, "user.name", "Only", replaceAll: true);
        var normalized = await _service.ReadDirectValuesAsync(_repository, "user.name", GitConfigScope.Repository);

        Assert.Single(normalized);
        Assert.Equal("Only", normalized[0].Value);

        await _service.UnsetAllAsync(_repository, GitConfigScope.Repository, "user.name");
        Assert.Empty(await _service.ReadDirectValuesAsync(_repository, "user.name", GitConfigScope.Repository));
    }

    [Fact]
    public async Task SnapshotPreservesOrderDuplicateValuesAndEmbeddedNewlines()
    {
        var multiline = "tool --arg='quoted value'\\nsecond line\\tlast";
        multiline = multiline.Replace("\\n", "\n").Replace("\\t", "\t");
        RunGit(_repositoryPath, "config", "--local", "--add", "difftool.MyTool.cmd", "first");
        RunGit(_repositoryPath, "config", "--local", "--add", "difftool.MyTool.cmd", multiline);
        RunGit(_repositoryPath, "config", "--local", "core.editor", "");

        var snapshot = await _service.ReadSnapshotAsync(_repository);

        var commands = snapshot.Values("DIFFTOOL.MyTool.CMD");
        Assert.Equal(2, commands.Count);
        Assert.Equal("first", commands[0].Value);
        Assert.Equal(multiline, commands[1].Value);
        Assert.Equal(multiline, snapshot.Effective("difftool.MyTool.cmd")?.Value);
        Assert.Null(snapshot.Effective("difftool.mytool.cmd")); // subsection names are case-sensitive
        Assert.Equal(string.Empty, snapshot.Effective("core.editor")?.Value);
        Assert.Null(snapshot.Effective("missing.key"));
    }

    [Fact]
    public async Task SnapshotMatchesGitCliAndTracksIncludeOriginsAndScopes()
    {
        var included = Path.Combine(_root, "included.gitconfig");
        File.WriteAllText(included, "[diff]\\n\\ttool = from-include\\n".Replace("\\n", "\n").Replace("\\t", "\t"));
        RunGit(_repositoryPath, "config", "--global", "include.path", included);
        RunGit(_repositoryPath, "config", "--local", "diff.guitool", "local-gui");
        RunGit(_repositoryPath, "config", "--local", "--add", "user.name", "one");
        RunGit(_repositoryPath, "config", "--local", "--add", "user.name", "two");

        var snapshot = await _service.ReadSnapshotAsync(_repository);
        var direct = await _service.ReadSnapshotAsync(_repository, includes: false);
        var expected = await _service.ReadEffectiveAsync(_repository, "diff.tool");
        var expectedLocal = await _service.ReadScopeAsync(_repository, "diff.guitool", GitConfigScope.Repository);

        Assert.Equal(expected, snapshot.Effective("diff.tool"));
        Assert.Equal(expectedLocal, snapshot.Scoped("diff.guitool", GitConfigSource.Repository));
        Assert.Contains("included.gitconfig", snapshot.Effective("diff.tool")?.Origin ?? string.Empty);
        Assert.Null(direct.Effective("diff.tool"));
        Assert.Equal(new[] { "one", "two" },
            snapshot.ScopedValues("user.name", GitConfigSource.Repository).Select(item => item.Value));
    }

    [Fact]
    public async Task SnapshotReflectsWorktreeAndExternalChangesWithoutStaleValues()
    {
        RunGit(_repositoryPath, "config", "--global", "core.editor", "global");
        RunGit(_repositoryPath, "config", "--local", "core.editor", "local");
        RunGit(_repositoryPath, "config", "--local", "extensions.worktreeConfig", "true");
        RunGit(_repositoryPath, "config", "--worktree", "core.editor", "worktree");

        var first = await _service.ReadSnapshotAsync(_repository);
        Assert.Equal("global", first.Scoped("core.editor", GitConfigSource.Global)?.Value);
        Assert.Equal("local", first.Scoped("core.editor", GitConfigSource.Repository)?.Value);
        Assert.Equal("worktree", first.Effective("core.editor")?.Value);
        Assert.Equal(GitConfigSource.Worktree, first.Effective("core.editor")?.Source);

        RunGit(_repositoryPath, "config", "--worktree", "core.editor", "changed");
        var second = await _service.ReadSnapshotAsync(_repository);
        Assert.Equal("changed", second.Effective("core.editor")?.Value);
        Assert.Equal("worktree", first.Effective("core.editor")?.Value);
    }

    [Fact]
    public async Task SnapshotHonorsConditionalOnBranchIncludes()
    {
        var included = Path.Combine(_root, "conditional.gitconfig");
        File.WriteAllText(included, "[diff]\\n\\ttool = conditional\\n".Replace("\\n", "\n").Replace("\\t", "\t"));
        RunGit(_repositoryPath, "config", "--global", "includeIf.onbranch:main.path", included);

        var snapshot = await _service.ReadSnapshotAsync(_repository);
        var expected = await _service.ReadEffectiveAsync(_repository, "diff.tool");

        Assert.Equal(expected, snapshot.Effective("diff.tool"));
        Assert.Equal("conditional", snapshot.Effective("diff.tool")?.Value);
        Assert.Contains("conditional.gitconfig", snapshot.Effective("diff.tool")?.Origin ?? string.Empty);
    }

    [Fact]
    public async Task SnapshotDoesNotLeakUnrelatedConfigurationIntoActivityHistory()
    {
        const string secret = "sensitive-value-must-not-appear";
        RunGit(_repositoryPath, "config", "--local", "demo.secret", secret);
        var history = new GitCommandActivityHistory();
        var service = new GitConfigService(GitTestServices.CreateExecutor(activitySink: history));

        var snapshot = await service.ReadSnapshotAsync(_repository);
        var command = Assert.Single(history.GetSnapshot(GitCommandFilter.AllCommands));

        Assert.Equal(secret, snapshot.Effective("demo.secret")?.Value);
        Assert.Equal(string.Empty, command.StandardOutput);
        Assert.Equal(string.Empty, command.StandardError);
        Assert.DoesNotContain(secret, command.DisplayCommand);
    }

    [Fact]
    public async Task SnapshotPropagatesMalformedConfigAndCancellation()
    {
        var configFile = Path.Combine(_repositoryPath, ".git", "config");
        File.AppendAllText(configFile, "\n[bad section\n");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ReadSnapshotAsync(_repository));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ReadSnapshotAsync(_repository, cancelled.Token));
    }

    public void Dispose()
    {
        foreach (var pair in _originalEnvironment)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        TestDirectory.Delete(_repositoryPath);
        TestDirectory.Delete(_root);
    }

    private void SetEnvironment(string name, string? value)
    {
        if (!_originalEnvironment.ContainsKey(name))
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

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
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {output}\n{error}");
    }
}
