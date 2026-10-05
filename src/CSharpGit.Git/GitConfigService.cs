using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class GitConfigService
{
    private readonly GitCommandExecutor _executor;

    internal GitConfigService(GitCommandExecutor executor) =>
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));

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
            ["config", "--includes", "--show-scope", "--show-origin", "--get", key]);
        if (result.ExitCode == 1) return null;
        if (result.ExitCode != 0) throw Failure("Could not read Git configuration", result);
        return ParseEffective(key, result.StandardOutput);
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
            ["config", "--includes", ScopeArgument(scope), "--show-origin", "--get", key]);
        if (result.ExitCode is 1 or 5 or 128) return null;
        if (result.ExitCode != 0) throw Failure("Could not read Git configuration", result);
        return ParseScoped(key, result.StandardOutput, SourceFor(scope));
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
            ["config", "--no-includes", ScopeArgument(scope), "--show-origin", "--get-all", key]);
        if (result.ExitCode is 1 or 5 or 128) return [];
        if (result.ExitCode != 0) throw Failure("Could not read Git configuration", result);

        var source = SourceFor(scope);
        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => ParseScopedLine(key, line, source))
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
        if (result.ExitCode != 0) throw Failure($"Could not save {key}", result);
    }

    internal async Task UnsetAllAsync(
        Repository? repository,
        GitConfigScope scope,
        string key,
        CancellationToken cancellationToken = default)
    {
        if ((await ReadDirectValuesAsync(repository, key, scope, cancellationToken)).Count == 0) return;

        var result = await RunAsync(
            repository,
            "GitConfigUnset",
            GitCommandKind.User,
            cancellationToken,
            ["config", ScopeArgument(scope), "--unset-all", key]);
        if (result.ExitCode is 0 or 1 or 5) return;
        throw Failure($"Could not remove {key}", result);
    }

    private Task<GitCommandResult> RunAsync(
        Repository? repository,
        string operation,
        GitCommandKind kind,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments) =>
        _executor.ExecuteForResultAsync(
            repository?.WorkingDirectory ?? Environment.CurrentDirectory,
            operation,
            kind,
            cancellationToken,
            null,
            arguments);

    private static GitConfigValue? ParseEffective(string key, string output)
    {
        var line = LastLine(output);
        if (line is null) return null;
        var parts = line.Split('\t');
        if (parts.Length >= 3)
            return new GitConfigValue(key, string.Join('\t', parts.Skip(2)), ParseSource(parts[0]), parts[1]);

        if (parts.Length == 2)
        {
            var prefix = parts[0].Trim();
            var separator = prefix.IndexOfAny([' ', '\t']);
            if (separator > 0)
                return new GitConfigValue(key, parts[1], ParseSource(prefix[..separator]), prefix[(separator + 1)..].Trim());
        }

        return new GitConfigValue(key, line, GitConfigSource.NotConfigured, null);
    }

    private static GitConfigValue ParseScoped(string key, string output, GitConfigSource source) =>
        ParseScopedLine(key, LastLine(output) ?? string.Empty, source);

    private static GitConfigValue ParseScopedLine(string key, string line, GitConfigSource source)
    {
        var separator = line.IndexOf('\t');
        return separator < 0
            ? new GitConfigValue(key, line, source, null)
            : new GitConfigValue(key, line[(separator + 1)..], source, line[..separator].Trim());
    }

    private static string? LastLine(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

    private static GitConfigSource ParseSource(string value) => value.Trim().ToLowerInvariant() switch
    {
        "command" => GitConfigSource.Command,
        "worktree" => GitConfigSource.Worktree,
        "local" => GitConfigSource.Repository,
        "global" => GitConfigSource.Global,
        "system" => GitConfigSource.System,
        _ => GitConfigSource.NotConfigured
    };

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
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? $"Git exited with code {result.ExitCode}."
            : $"Git exited with code {result.ExitCode}. {result.StandardError.Trim()}";
        return new InvalidOperationException($"{prefix}. {detail}");
    }
}
