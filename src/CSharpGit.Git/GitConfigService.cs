using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitConfigService
{
    private readonly GitCommandExecutor _executor;

    internal GitConfigService(GitCommandExecutor executor) =>
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    // --list uses a different NUL record format from --get and --get-all.
    internal async Task<GitConfigSnapshot> ReadSnapshotAsync(
        Repository? repository,
        CancellationToken cancellationToken = default,
        bool includes = true)
    {
        var arguments = new[]
        {
            "config", "--list", "--null", "--show-origin", "--show-scope",
            includes ? "--includes" : "--no-includes"
        };
        var result = await _executor.ExecuteForResultSensitiveAsync(
            repository?.WorkingDirectory ?? Environment.CurrentDirectory,
            "GitConfigSnapshot",
            cancellationToken,
            arguments);
        if (result.ExitCode != 0) throw Failure("Could not read Git configuration", result);
        return GitConfigSnapshot.Parse(result.StandardOutput);
    }

    internal async Task<GitConfigValue?> ReadEffectiveAsync(
        Repository? repository,
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        var result = await RunAsync(
            repository,
            "GitConfigRead",
            GitCommandKind.Internal,
            cancellationToken,
            ["config", "--null", "--includes", "--show-scope", "--show-origin", "--get", key]);
        if (result.ExitCode == 1) return null;
        if (result.ExitCode != 0) throw Failure("Could not read Git configuration", result);
        var entry = GitConfigGetParser.ParseOne(result.StandardOutput);
        return new GitConfigValue(key, entry.Value, entry.Scope, entry.Origin);
    }

    internal async Task<GitConfigValue?> ReadScopeAsync(
        Repository? repository,
        string key,
        GitConfigScope scope,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(repository, scope);
        ValidateKey(key);
        var result = await RunAsync(
            repository,
            "GitConfigReadScope",
            GitCommandKind.Internal,
            cancellationToken,
            ["config", "--null", "--includes", ScopeArgument(scope), "--show-scope", "--show-origin", "--get", key]);
        if (result.ExitCode is 1 or 5 or 128) return null;
        if (result.ExitCode != 0) throw Failure("Could not read Git configuration", result);
        var entry = GitConfigGetParser.ParseOne(result.StandardOutput);
        return new GitConfigValue(key, entry.Value, SourceFor(scope), entry.Origin);
    }

    internal async Task<GitConfigValue?> ReadDirectScopeAsync(
        Repository? repository,
        string key,
        GitConfigScope scope,
        CancellationToken cancellationToken = default)
    {
        var values = await ReadDirectValuesAsync(repository, key, scope, cancellationToken);
        return values.LastOrDefault();
    }

    internal async Task<IReadOnlyList<GitConfigValue>> ReadDirectValuesAsync(
        Repository? repository,
        string key,
        GitConfigScope scope,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(repository, scope);
        ValidateKey(key);
        var result = await RunAsync(
            repository,
            "GitConfigReadDirectScope",
            GitCommandKind.Internal,
            cancellationToken,
            ["config", "--null", "--no-includes", ScopeArgument(scope), "--show-scope", "--show-origin", "--get-all", key]);
        if (result.ExitCode is 1 or 5 or 128) return [];
        if (result.ExitCode != 0) throw Failure("Could not read Git configuration", result);

        var source = SourceFor(scope);
        return GitConfigGetParser.ParseAll(result.StandardOutput)
            .Select(entry => new GitConfigValue(key, entry.Value, source, entry.Origin))
            .ToArray();
    }

    internal async Task SetValueAsync(
        Repository? repository,
        GitConfigScope scope,
        string key,
        string value,
        bool replaceAll,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(repository, scope);
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);

        var arguments = new List<string> { "config", ScopeArgument(scope) };
        if (replaceAll) arguments.Add("--replace-all");
        arguments.Add(key);
        arguments.Add(value);

        var result = await RunAsync(repository, "GitConfigWrite", GitCommandKind.User, cancellationToken, arguments);
        if (result.ExitCode != 0) throw Failure("Could not save Git configuration", result);
    }

    internal async Task UnsetAllAsync(
        Repository? repository,
        GitConfigScope scope,
        string key,
        CancellationToken cancellationToken = default)
    {
        if ((await ReadDirectValuesAsync(repository, key, scope, cancellationToken)).Count == 0) return;
        await UnsetAllWithoutReadAsync(repository, scope, key, cancellationToken);
    }

    // Used only when the caller already read the key. Git's 1/5 exit statuses
    // represent an absent key (including a key supplied only by an included file).
    internal async Task UnsetAllWithoutReadAsync(
        Repository? repository,
        GitConfigScope scope,
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(repository, scope);
        ValidateKey(key);
        var result = await RunAsync(
            repository,
            "GitConfigUnset",
            GitCommandKind.User,
            cancellationToken,
            ["config", ScopeArgument(scope), "--unset-all", key]);
        if (result.ExitCode is 0 or 1 or 5) return;
        throw Failure("Could not remove Git configuration", result);
    }

    private Task<GitCommandResult> RunAsync(
        Repository? repository,
        string operation,
        GitCommandKind kind,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments) =>
        _executor.ExecuteForResultSensitiveAsync(
            repository?.WorkingDirectory ?? Environment.CurrentDirectory,
            operation,
            cancellationToken,
            arguments);

    private static GitConfigSource SourceFor(GitConfigScope scope) => scope switch
    {
        GitConfigScope.Worktree => GitConfigSource.Worktree,
        GitConfigScope.Repository => GitConfigSource.Repository,
        GitConfigScope.Global => GitConfigSource.Global,
        GitConfigScope.System => GitConfigSource.System,
        _ => GitConfigSource.NotConfigured
    };

    private static string ScopeArgument(GitConfigScope scope) => scope switch
    {
        GitConfigScope.Worktree => "--worktree",
        GitConfigScope.Repository => "--local",
        GitConfigScope.Global => "--global",
        GitConfigScope.System => "--system",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    private static void ValidateScope(Repository? repository, GitConfigScope scope)
    {
        if (scope is GitConfigScope.Repository or GitConfigScope.Worktree && repository is null)
            throw new InvalidOperationException("Open a repository before accessing repository Git configuration.");
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A Git configuration key is required.", nameof(key));
    }

    private static InvalidOperationException Failure(string prefix, GitCommandResult result)
    {
        return new InvalidOperationException($"{prefix}. Git exited with code {result.ExitCode}; sensitive diagnostics omitted.");
    }
}
