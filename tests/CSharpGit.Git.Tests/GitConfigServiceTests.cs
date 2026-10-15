using System.Diagnostics;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.Controls;

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
    public async Task SensitiveConfigurationOperationsNeverPublishArbitraryValues()
    {
        var history = new GitCommandActivityHistory();
        var publishedEvents = new System.Collections.Concurrent.ConcurrentQueue<string>();
        history.Changed += (_, args) => publishedEvents.Enqueue(
            (args.Activity?.DisplayCommand ?? string.Empty) +
            (args.Activity?.StandardOutput ?? string.Empty) +
            (args.Activity?.StandardError ?? string.Empty) +
            (args.OutputChunk ?? string.Empty));
        var executor = GitTestServices.CreateExecutor(activitySink: history);
        var config = new GitConfigService(executor);
        var stateService = new GitRepositoryStateService(executor);
        const string secret = "very-unusual-key-value-872612\nsecond-secret-line\n";
        const string urlSecret = "url-credential-872612";

        await config.SetValueAsync(_repository, GitConfigScope.Repository, "demo.misc", secret, true);
        await config.SetValueAsync(_repository, GitConfigScope.Repository, "remote.origin.url",
            $"https://user:{urlSecret}@example.invalid/repo.git", true);
        var direct = await config.ReadDirectValuesAsync(_repository, "demo.misc", GitConfigScope.Repository);
        var last = await config.ReadDirectScopeAsync(_repository, "demo.misc", GitConfigScope.Repository);
        var scoped = await config.ReadScopeAsync(_repository, "demo.misc", GitConfigScope.Repository);
        var effective = await config.ReadEffectiveAsync(_repository, "demo.misc");
        var snapshot = await config.ReadSnapshotAsync(_repository);
        var state = await stateService.ReadAsync(_repository);

        Assert.Equal(secret, Assert.Single(direct).Value);
        Assert.Equal(secret, last?.Value);
        Assert.Equal(secret, scoped?.Value);
        Assert.Equal(secret, effective?.Value);
        Assert.Equal(secret, snapshot.Effective("demo.misc")?.Value);
        Assert.Equal(secret, state.LocalConfiguration["demo.misc"]);
        Assert.Contains(state.Refs.Remotes, remote => remote.FetchUrl.Contains(urlSecret, StringComparison.Ordinal));

        await config.UnsetAllWithoutReadAsync(_repository, GitConfigScope.Repository, "demo.misc");
        var activities = history.GetSnapshot(GitCommandFilter.AllCommands);
        Assert.NotEmpty(activities);
        var diagnostics = string.Join("\n", activities.Select(a =>
            a.DisplayCommand + "\n" + a.StandardOutput + "\n" + a.StandardError));
        Assert.DoesNotContain(secret, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(urlSecret, diagnostics, StringComparison.Ordinal);
        var changed = string.Join("\n", publishedEvents);
        Assert.DoesNotContain(secret, changed, StringComparison.Ordinal);
        Assert.DoesNotContain(urlSecret, changed, StringComparison.Ordinal);
        var clipboard = string.Join("\n", activities.Select(GitConsoleClipboardText.Build));
        Assert.DoesNotContain(secret, clipboard, StringComparison.Ordinal);
        Assert.DoesNotContain(urlSecret, clipboard, StringComparison.Ordinal);
        Assert.Contains(activities, a => a.DisplayCommand.Contains("sensitive Git arguments omitted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedSensitiveCommandNeverExposesStderrOrInputValue()
    {
        var history = new GitCommandActivityHistory();
        var config = new GitConfigService(GitTestServices.CreateExecutor(activitySink: history));
        const string secret = "private-arbitrary-value-44917";
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => config.SetValueAsync(_repository, GitConfigScope.Repository,
                "invalid-key", secret, true));
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("code", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, string.Join("\n",
            history.GetSnapshot(GitCommandFilter.AllCommands).Select(x =>
                x.DisplayCommand + x.StandardOutput + x.StandardError)), StringComparison.Ordinal);
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

        var actual = snapshot.Effective("diff.tool");
        var actualLocal = snapshot.Scoped("diff.guitool", GitConfigSource.Repository);
        // Git's regular --show-origin quotes/escapes Windows paths, while -z
        // returns the raw origin field. Compare their meaning, not the display syntax.
        Assert.Equal(expected?.Key, actual?.Key);
        Assert.Equal(expected?.Value, actual?.Value);
        Assert.Equal(expected?.Source, actual?.Source);
        Assert.Equal(expectedLocal?.Key, actualLocal?.Key);
        Assert.Equal(expectedLocal?.Value, actualLocal?.Value);
        Assert.Equal(expectedLocal?.Source, actualLocal?.Source);
        Assert.Contains("included.gitconfig", expected?.Origin ?? string.Empty);
        Assert.Contains("included.gitconfig", actual?.Origin ?? string.Empty);
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

        var actual = snapshot.Effective("diff.tool");
        Assert.Equal(expected?.Value, actual?.Value);
        Assert.Equal(expected?.Source, actual?.Source);
        Assert.Equal("conditional", actual?.Value);
        Assert.Contains("conditional.gitconfig", actual?.Origin ?? string.Empty);
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


    [Fact]
    public async Task MultilineSingleKeyIsPreservedThroughAllFourReadMethods()
    {
        const string key = "difftool.MyTool.cmd";
        const string value = "first line\nsecond line\nthird line";
        RunGit(_repositoryPath, "config", "--local", key, value);
        var expected = Assert.Single(ReadGitRecords("--local", "--get", key)).Value;

        var effective = await _service.ReadEffectiveAsync(_repository, key);
        var scoped = await _service.ReadScopeAsync(_repository, key, GitConfigScope.Repository);
        var direct = await _service.ReadDirectValuesAsync(_repository, key, GitConfigScope.Repository);
        var last = await _service.ReadDirectScopeAsync(_repository, key, GitConfigScope.Repository);

        Assert.Equal(expected, effective?.Value);
        Assert.Equal(expected, scoped?.Value);
        Assert.Equal(expected, Assert.Single(direct).Value);
        Assert.Equal(expected, last?.Value);
    }

    [Fact]
    public async Task DirectValuesPreserveDuplicatesAndEmbeddedLineBreaks()
    {
        const string key = "demo.multi";
        var values = new[] { "first", "second\ncontinued", "second\ncontinued", "third" };
        foreach (var value in values)
            RunGit(_repositoryPath, "config", "--local", "--add", key, value);

        var cli = ReadGitRecords("--local", "--get-all", key);
        var direct = await _service.ReadDirectValuesAsync(_repository, key, GitConfigScope.Repository);
        var last = await _service.ReadDirectScopeAsync(_repository, key, GitConfigScope.Repository);
        var effective = await _service.ReadEffectiveAsync(_repository, key);

        Assert.Equal(values, cli.Select(item => item.Value));
        Assert.Equal(values, direct.Select(item => item.Value));
        Assert.Equal(cli.Select(item => item.Origin), direct.Select(item => item.Origin));
        Assert.All(direct, item => Assert.Equal(GitConfigSource.Repository, item.Source));
        Assert.Equal("third", last?.Value);
        Assert.Equal(Assert.Single(ReadGitRecords("--get", key)).Value, effective?.Value);
    }

    [Fact]
    public async Task EmptyValueIsDifferentFromMissingKeyEvenWhenRemoving()
    {
        const string key = "demo.empty";
        Assert.Null(await _service.ReadEffectiveAsync(_repository, key));
        Assert.Null(await _service.ReadScopeAsync(_repository, key, GitConfigScope.Repository));
        Assert.Null(await _service.ReadDirectScopeAsync(_repository, key, GitConfigScope.Repository));
        Assert.Empty(await _service.ReadDirectValuesAsync(_repository, key, GitConfigScope.Repository));

        RunGit(_repositoryPath, "config", "--local", key, "");
        Assert.Equal("", (await _service.ReadEffectiveAsync(_repository, key))?.Value);
        Assert.Equal("", (await _service.ReadScopeAsync(_repository, key, GitConfigScope.Repository))?.Value);
        Assert.Equal("", (await _service.ReadDirectScopeAsync(_repository, key, GitConfigScope.Repository))?.Value);
        Assert.Equal("", Assert.Single(await _service.ReadDirectValuesAsync(
            _repository, key, GitConfigScope.Repository)).Value);

        await _service.UnsetAllAsync(_repository, GitConfigScope.Repository, key);
        Assert.Empty(await _service.ReadDirectValuesAsync(_repository, key, GitConfigScope.Repository));
    }

    [Theory]
    [InlineData("value\n")]
    [InlineData("\nvalue")]
    [InlineData("value\n\n")]
    [InlineData("first\r\nsecond")]
    [InlineData("first\rsecond")]
    [InlineData(" value ")]
    [InlineData("\tvalue\t")]
    [InlineData("quoted ' \" slash \\ русский 🚀")]
    public async Task ValuesPreserveExactlyWhatGitReturns(string input)
    {
        const string key = "demo.boundary";
        RunGit(_repositoryPath, "config", "--local", key, input);
        var cli = Assert.Single(ReadGitRecords("--local", "--get", key));

        Assert.Equal(cli.Value, (await _service.ReadEffectiveAsync(_repository, key))?.Value);
        Assert.Equal(cli.Value, (await _service.ReadScopeAsync(
            _repository, key, GitConfigScope.Repository))?.Value);
        var direct = Assert.Single(await _service.ReadDirectValuesAsync(
            _repository, key, GitConfigScope.Repository));
        Assert.Equal(cli.Value, direct.Value);
        Assert.Equal(cli.Origin, direct.Origin);
        Assert.Equal(cli.Value, (await _service.ReadDirectScopeAsync(
            _repository, key, GitConfigScope.Repository))?.Value);
    }

    [Fact]
    public async Task ScopedWorktreeSourceUsesRequestedScopeRatherThanReportedGitScope()
    {
        const string key = "demo.worktree";
        RunGit(_repositoryPath, "config", "--local", "extensions.worktreeConfig", "true");
        RunGit(_repositoryPath, "config", "--worktree", key, "worktree\nvalue");

        var gitRecord = Assert.Single(ReadGitRecords("--worktree", "--get", key));
        var scoped = await _service.ReadScopeAsync(_repository, key, GitConfigScope.Worktree);
        var direct = Assert.Single(await _service.ReadDirectValuesAsync(
            _repository, key, GitConfigScope.Worktree));
        var effective = await _service.ReadEffectiveAsync(_repository, key);

        Assert.Equal(gitRecord.Value, scoped?.Value);
        Assert.Equal(gitRecord.Value, direct.Value);
        Assert.Equal(gitRecord.Origin, scoped?.Origin);
        Assert.Equal(gitRecord.Origin, direct.Origin);
        Assert.Equal(GitConfigSource.Worktree, scoped?.Source);
        Assert.Equal(GitConfigSource.Worktree, direct.Source);
        Assert.Equal(GitConfigSource.Worktree, effective?.Source);
        Assert.Equal(gitRecord.Value, effective?.Value);
    }

    [Fact]
    public async Task IncludedConfigPreservesOriginalPathAndDirectReadsSkipIncludes()
    {
        const string key = "demo.shared";
        var name = OperatingSystem.IsWindows()
            ? "included file with spaces.gitconfig"
            : "included file\twith newline\nand spaces.gitconfig";
        var included = Path.Combine(_root, name);
        File.WriteAllText(included, "[demo]\n\tshared = from include\n");
        RunGit(_repositoryPath, "config", "--local", "include.path", included);

        var cli = Assert.Single(ReadGitRecords("--includes", "--local", "--get", key));
        var scoped = await _service.ReadScopeAsync(_repository, key, GitConfigScope.Repository);
        var effective = await _service.ReadEffectiveAsync(_repository, key);

        Assert.Equal("from include", scoped?.Value);
        Assert.Equal(cli.Value, effective?.Value);
        Assert.Equal(cli.Origin, scoped?.Origin);
        Assert.Equal(cli.Origin, effective?.Origin);
        Assert.Equal(GitConfigSource.Repository, scoped?.Source);
        Assert.Equal(GitConfigSource.Repository, effective?.Source);
        Assert.Contains("with", scoped?.Origin ?? string.Empty);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Contains('\t', scoped!.Origin!);
            Assert.Contains('\n', scoped.Origin!);
        }
        Assert.Empty(await _service.ReadDirectValuesAsync(_repository, key, GitConfigScope.Repository));
        Assert.Null(await _service.ReadDirectScopeAsync(_repository, key, GitConfigScope.Repository));

        RunGit(_repositoryPath, "config", "--local", "--add", key, "direct");
        var direct = Assert.Single(await _service.ReadDirectValuesAsync(
            _repository, key, GitConfigScope.Repository));
        Assert.Equal("direct", direct.Value);
        Assert.Equal(Assert.Single(ReadGitRecords("--includes", "--local", "--get", key)).Value,
            (await _service.ReadScopeAsync(_repository, key, GitConfigScope.Repository))?.Value);
    }

    [Fact]
    public async Task GlobalScopedReadsPreserveSourceAndActualIncludedOrigin()
    {
        const string key = "demo.global";
        var included = Path.Combine(_root, "global included.gitconfig");
        File.WriteAllText(included, "[demo]\n\tglobal = global include\n");
        RunGit(_repositoryPath, "config", "--global", "include.path", included);

        var scoped = await _service.ReadScopeAsync(_repository, key, GitConfigScope.Global);
        var effective = await _service.ReadEffectiveAsync(_repository, key);
        Assert.Equal("global include", scoped?.Value);
        Assert.Equal(GitConfigSource.Global, scoped?.Source);
        Assert.Equal(GitConfigSource.Global, effective?.Source);
        Assert.Equal(Assert.Single(ReadGitRecords("--includes", "--global", "--get", key)).Origin,
            scoped?.Origin);
        Assert.Null(await _service.ReadDirectScopeAsync(_repository, key, GitConfigScope.Global));
        Assert.Empty(await _service.ReadDirectValuesAsync(_repository, key, GitConfigScope.Global));
    }

    [Fact]
    public async Task ReplacingAndRemovingMultilineDuplicatesDoesNotLoseEntries()
    {
        const string key = "demo.rewrite";
        RunGit(_repositoryPath, "config", "--local", "--add", key, "first\nsecond\n");
        RunGit(_repositoryPath, "config", "--local", "--add", key, "last\nline\n");
        var before = await _service.ReadDirectValuesAsync(_repository, key, GitConfigScope.Repository);
        Assert.Equal(2, before.Count);
        Assert.Equal(Assert.Single(ReadGitRecords("--local", "--get", key)).Value,
            (await _service.ReadDirectScopeAsync(_repository, key, GitConfigScope.Repository))?.Value);

        await _service.SetValueAsync(_repository, GitConfigScope.Repository, key, "replacement\n", replaceAll: true);
        Assert.Equal(Assert.Single(ReadGitRecords("--local", "--get", key)).Value,
            Assert.Single(await _service.ReadDirectValuesAsync(_repository, key, GitConfigScope.Repository)).Value);

        await _service.UnsetAllAsync(_repository, GitConfigScope.Repository, key);
        Assert.Null(await _service.ReadDirectScopeAsync(_repository, key, GitConfigScope.Repository));
        Assert.Empty(await _service.ReadDirectValuesAsync(_repository, key, GitConfigScope.Repository));
    }

    [Fact]
    public async Task ScopedReadsPreserveGitFailureAndCancellationContracts()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ReadEffectiveAsync(_repository, "demo.key", cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ReadScopeAsync(_repository, "demo.key", GitConfigScope.Repository, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ReadDirectValuesAsync(_repository, "demo.key", GitConfigScope.Repository, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ReadDirectScopeAsync(_repository, "demo.key", GitConfigScope.Repository, cancelled.Token));

        File.AppendAllText(Path.Combine(_repositoryPath, ".git", "config"), "\n[bad section\n");
        Assert.Null(await _service.ReadScopeAsync(_repository, "demo.key", GitConfigScope.Repository));
        Assert.Empty(await _service.ReadDirectValuesAsync(_repository, "demo.key", GitConfigScope.Repository));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ReadEffectiveAsync(_repository, "demo.key"));
        Assert.Contains("sensitive diagnostics omitted", error.Message);
    }

    // Independent --null Git CLI oracle: the test deliberately does not use the production parser.
    private IReadOnlyList<(string Scope, string Origin, string Value)> ReadGitRecords(
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("config");
        startInfo.ArgumentList.Add("--null");
        startInfo.ArgumentList.Add("--show-scope");
        startInfo.ArgumentList.Add("--show-origin");
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"Git CLI read failed (exit {process.ExitCode}).");
        Assert.Equal('\0', output[^1]);

        var fields = output.Split('\0');
        Assert.Equal(0, (fields.Length - 1) % 3);
        var entries = new List<(string Scope, string Origin, string Value)>();
        for (var i = 0; i < fields.Length - 1; i += 3)
            entries.Add((fields[i], fields[i + 1], fields[i + 2]));
        return entries;
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
