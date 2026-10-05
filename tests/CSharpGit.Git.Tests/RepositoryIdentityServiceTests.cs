using System.Diagnostics;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

[Collection(GitToolsEnvironmentCollection.CollectionName)]
public sealed class RepositoryIdentityServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-identity-{Guid.NewGuid():N}");
    private readonly string _repositoryPath;
    private readonly string _globalConfig;
    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);
    private readonly Repository _repository;
    private readonly GitCommandExecutor _executor;
    private readonly RepositoryIdentityService _service;

    public RepositoryIdentityServiceTests()
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

        RunGit("init", "-b", "main");
        _repository = new Repository(
            Path.GetFullPath(_repositoryPath),
            Path.GetFullPath(_repositoryPath),
            Path.GetFullPath(Path.Combine(_repositoryPath, ".git")),
            false);
        _executor = GitTestServices.CreateExecutor();
        _service = new RepositoryIdentityService(new GitConfigService(_executor));
    }

    [Fact]
    public async Task GlobalIdentityIsInheritedWithoutRepositoryOverrides()
    {
        SetGlobalIdentity("Global User", "global@example.com");

        var snapshot = await _service.ReadAsync(_repository);

        Assert.Equal("Global User", snapshot.Name.EffectiveValue);
        Assert.Equal(GitConfigSource.Global, snapshot.Name.EffectiveSource);
        Assert.Null(snapshot.Name.RepositoryValue);
        Assert.False(snapshot.Name.HasRepositoryOverride);
        Assert.Equal("global@example.com", snapshot.Email.EffectiveValue);
        Assert.Equal(GitConfigSource.Global, snapshot.Email.EffectiveSource);
        Assert.Null(snapshot.Email.RepositoryValue);
    }

    [Fact]
    public async Task FullAndPartialRepositoryOverridesResolveIndependently()
    {
        SetGlobalIdentity("Global User", "global@example.com");
        RunGit("config", "--local", "user.name", "Local User");

        var partial = await _service.ReadAsync(_repository);

        Assert.Equal("Local User", partial.Name.EffectiveValue);
        Assert.Equal(GitConfigSource.Repository, partial.Name.EffectiveSource);
        Assert.Equal("Local User", partial.Name.RepositoryValue);
        Assert.Equal("global@example.com", partial.Email.EffectiveValue);
        Assert.Equal(GitConfigSource.Global, partial.Email.EffectiveSource);

        RunGit("config", "--local", "user.email", "local@example.com");
        var full = await _service.ReadAsync(_repository);
        Assert.Equal("local@example.com", full.Email.RepositoryValue);
        Assert.Equal(GitConfigSource.Repository, full.Email.EffectiveSource);
    }

    [Fact]
    public async Task SaveNameOnlyDoesNotMaterializeOrRewriteEmail()
    {
        SetGlobalIdentity("Global User", "global@example.com");

        await _service.SaveAsync(
            _repository,
            new RepositoryIdentityEdit(true, "Local User", false, "ignored@example.com"));

        Assert.Equal("Local User", ReadGit("config", "--local", "--get", "user.name"));
        Assert.Equal(string.Empty, ReadGitAllowFailure("config", "--local", "--get", "user.email"));
    }

    [Fact]
    public async Task SaveEmailOnlyDoesNotMaterializeOrRewriteName()
    {
        SetGlobalIdentity("Global User", "global@example.com");

        await _service.SaveAsync(
            _repository,
            new RepositoryIdentityEdit(false, "Ignored", true, "local@example.com"));

        Assert.Equal(string.Empty, ReadGitAllowFailure("config", "--local", "--get", "user.name"));
        Assert.Equal("local@example.com", ReadGit("config", "--local", "--get", "user.email"));
    }

    [Fact]
    public async Task ExplicitSameAsGlobalValueCreatesRepositoryOverride()
    {
        SetGlobalIdentity("Global User", "global@example.com");

        await _service.SaveAsync(
            _repository,
            new RepositoryIdentityEdit(true, "Global User", false, null));

        var snapshot = await _service.ReadAsync(_repository);
        Assert.True(snapshot.Name.HasRepositoryOverride);
        Assert.Equal("Global User", snapshot.Name.RepositoryValue);
        Assert.Equal(GitConfigSource.Repository, snapshot.Name.EffectiveSource);
    }

    [Fact]
    public async Task EmptyExistingValueRemovesOnlyRequestedOverrideAndNoOpEmptyDoesNotWrite()
    {
        RunGit("config", "--local", "user.name", "Local User");
        RunGit("config", "--local", "user.email", "local@example.com");

        await _service.SaveAsync(
            _repository,
            new RepositoryIdentityEdit(true, "   ", false, null));

        Assert.Equal(string.Empty, ReadGitAllowFailure("config", "--local", "--get", "user.name"));
        Assert.Equal("local@example.com", ReadGit("config", "--local", "--get", "user.email"));

        var configPath = Path.Combine(_repositoryPath, ".git", "config");
        var sentinel = new DateTime(2002, 3, 4, 5, 6, 8, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(configPath, sentinel);

        await _service.SaveAsync(
            _repository,
            new RepositoryIdentityEdit(true, string.Empty, false, null));

        Assert.Equal(sentinel, File.GetLastWriteTimeUtc(configPath));
    }

    [Fact]
    public async Task RemoveNameAndEmailOverridesAreIndependent()
    {
        RunGit("config", "--local", "user.name", "Local User");
        RunGit("config", "--local", "user.email", "local@example.com");

        await _service.RemoveOverrideAsync(_repository, RepositoryIdentityField.Name);

        Assert.Equal(string.Empty, ReadGitAllowFailure("config", "--local", "--get", "user.name"));
        Assert.Equal("local@example.com", ReadGit("config", "--local", "--get", "user.email"));

        await _service.RemoveOverrideAsync(_repository, RepositoryIdentityField.Email);
        Assert.Equal(string.Empty, ReadGitAllowFailure("config", "--local", "--get", "user.email"));
    }

    [Fact]
    public async Task DuplicateLocalKeysAreNormalizedOnSaveAndAllRemovedOnRemoval()
    {
        RunGit("config", "--local", "--add", "user.name", "First");
        RunGit("config", "--local", "--add", "user.name", "Second");

        await _service.SaveAsync(
            _repository,
            new RepositoryIdentityEdit(true, "Normalized", false, null));

        Assert.Equal(new[] { "Normalized" }, ReadGitLines("config", "--local", "--get-all", "user.name"));

        RunGit("config", "--local", "--add", "user.name", "Duplicate Again");
        await _service.RemoveOverrideAsync(_repository, RepositoryIdentityField.Name);
        Assert.Empty(ReadGitLinesAllowFailure("config", "--local", "--get-all", "user.name"));
    }

    [Fact]
    public async Task IncludedRepositoryValueIsNotRemovableOverrideAndReturnsAfterDirectRemoval()
    {
        var included = Path.Combine(_root, "included.config");
        File.WriteAllText(included, "[user]\n\tname = Included User\n");
        RunGit("config", "--local", "include.path", included);

        var initial = await _service.ReadAsync(_repository);
        Assert.Equal("Included User", initial.Name.EffectiveValue);
        Assert.Equal(GitConfigSource.Repository, initial.Name.EffectiveSource);
        Assert.Contains("included.config", initial.Name.EffectiveOrigin ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(initial.Name.HasRepositoryOverride);
        Assert.Null(initial.Name.RepositoryValue);

        await _service.SaveAsync(
            _repository,
            new RepositoryIdentityEdit(true, "Direct User", false, null));

        var direct = await _service.ReadAsync(_repository);
        Assert.Equal("Direct User", direct.Name.EffectiveValue);
        Assert.True(direct.Name.HasRepositoryOverride);
        Assert.Equal("Direct User", direct.Name.RepositoryValue);

        await _service.RemoveOverrideAsync(_repository, RepositoryIdentityField.Name);

        var fallback = await _service.ReadAsync(_repository);
        Assert.Equal("Included User", fallback.Name.EffectiveValue);
        Assert.False(fallback.Name.HasRepositoryOverride);
        Assert.Equal("[user]\n\tname = Included User\n", File.ReadAllText(included));
    }

    [Fact]
    public async Task WorktreeValueRemainsEffectiveAfterSavingRepositoryOverride()
    {
        RunGit("config", "extensions.worktreeConfig", "true");
        RunGit("config", "--worktree", "user.name", "Worktree User");

        await _service.SaveAsync(
            _repository,
            new RepositoryIdentityEdit(true, "Local User", false, null));

        var snapshot = await _service.ReadAsync(_repository);
        Assert.Equal("Worktree User", snapshot.Name.EffectiveValue);
        Assert.Equal(GitConfigSource.Worktree, snapshot.Name.EffectiveSource);
        Assert.Equal("Local User", snapshot.Name.RepositoryValue);
        Assert.Equal("Worktree User", ReadGit("config", "--worktree", "--get", "user.name"));
    }

    [Fact]
    public async Task CommandValueRemainsEffectiveAfterSavingRepositoryOverride()
    {
        SetEnvironment("GIT_CONFIG_COUNT", "1");
        SetEnvironment("GIT_CONFIG_KEY_0", "user.name");
        SetEnvironment("GIT_CONFIG_VALUE_0", "Command User");

        await _service.SaveAsync(
            _repository,
            new RepositoryIdentityEdit(true, "Local User", false, null));

        var snapshot = await _service.ReadAsync(_repository);
        Assert.Equal("Command User", snapshot.Name.EffectiveValue);
        Assert.Equal(GitConfigSource.Command, snapshot.Name.EffectiveSource);
        Assert.Equal("Local User", snapshot.Name.RepositoryValue);
    }

    [Theory]
    [InlineData("john@")]
    [InlineData("@company.com")]
    [InlineData("john company.com")]
    public async Task InvalidEmailIsRejectedWithoutWriting(string email)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SaveAsync(
                _repository,
                new RepositoryIdentityEdit(false, null, true, email)));

        Assert.Equal(string.Empty, ReadGitAllowFailure("config", "--local", "--get", "user.email"));
    }

    [Theory]
    [InlineData("john@localhost")]
    [InlineData("john+work@example.com")]
    public async Task SoftEmailValidationAcceptsUsefulGitAddresses(string email)
    {
        await _service.SaveAsync(
            _repository,
            new RepositoryIdentityEdit(false, null, true, email));

        Assert.Equal(email, ReadGit("config", "--local", "--get", "user.email"));
    }

    [Fact]
    public async Task OrdinaryCommitUsesRepositoryLocalIdentityWithoutAuthorOverrides()
    {
        SetGlobalIdentity("Global User", "global@example.com");
        await _service.SaveAsync(
            _repository,
            new RepositoryIdentityEdit(true, "Local User", true, "local@example.com"));

        File.WriteAllText(Path.Combine(_repositoryPath, "commit.txt"), "content\n");
        RunGit("add", "--", "commit.txt");

        var workingTree = new GitWorkingTreeService(_executor);
        await workingTree.CommitAsync(_repository, "identity integration");

        var metadata = ReadGitLines("show", "-s", "--format=%an%n%ae%n%cn%n%ce", "HEAD");
        Assert.Equal(new[]
        {
            "Local User",
            "local@example.com",
            "Local User",
            "local@example.com"
        }, metadata);
    }

    public void Dispose()
    {
        foreach (var pair in _originalEnvironment)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        TestDirectory.Delete(_root);
    }

    private void SetGlobalIdentity(string name, string email)
    {
        RunGit("config", "--global", "user.name", name);
        RunGit("config", "--global", "user.email", email);
    }

    private void SetEnvironment(string name, string? value)
    {
        if (!_originalEnvironment.ContainsKey(name))
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private void RunGit(params string[] arguments)
    {
        var result = RunGitCore(arguments);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {result.Output}\n{result.Error}");
    }

    private string ReadGit(params string[] arguments)
    {
        var result = RunGitCore(arguments);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {result.Output}\n{result.Error}");
        return result.Output.Trim();
    }

    private string ReadGitAllowFailure(params string[] arguments)
    {
        var result = RunGitCore(arguments);
        return result.ExitCode == 0 ? result.Output.Trim() : string.Empty;
    }

    private string[] ReadGitLines(params string[] arguments) =>
        ReadGit(arguments).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private string[] ReadGitLinesAllowFailure(params string[] arguments)
    {
        var value = ReadGitAllowFailure(arguments);
        return value.Length == 0
            ? []
            : value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private (int ExitCode, string Output, string Error) RunGitCore(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repositoryPath,
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
        return (process.ExitCode, output, error);
    }
}
