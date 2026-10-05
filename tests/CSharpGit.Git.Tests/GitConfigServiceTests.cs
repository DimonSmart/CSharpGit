using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

[Collection(GitToolsEnvironmentCollection.CollectionName)]
public sealed class GitConfigServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-git-config-{Guid.NewGuid():N}");
    private readonly string _repositoryPath;
    private readonly string _globalConfig;
    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);
    private readonly Repository _repository;
    private readonly GitConfigService _service;

    public GitConfigServiceTests()
    {
        _repositoryPath = Path.Combine(_root, "repo");
        _globalConfig = Path.Combine(_root, "global.gitconfig");
        Directory.CreateDirectory(_repositoryPath);
        File.WriteAllText(_globalConfig, string.Empty);

        SetEnvironment("GIT_CONFIG_GLOBAL", _globalConfig);
        SetEnvironment("GIT_CONFIG_NOSYSTEM", "1");
        SetEnvironment("GIT_CONFIG_SYSTEM", null);
        SetEnvironment("GIT_CONFIG_COUNT", null);
        SetEnvironment("GIT_CONFIG_KEY_0", null);
        SetEnvironment("GIT_CONFIG_VALUE_0", null);

        RunGit(_repositoryPath, "init", "-b", "main");
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

    public void Dispose()
    {
        foreach (var pair in _originalEnvironment)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
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
