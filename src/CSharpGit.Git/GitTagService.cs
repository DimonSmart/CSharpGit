using System.Globalization;
using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitTagService : ITagService
{
    private const string DefaultTagSort = "-version:refname";
    private readonly GitCommandExecutor _executor;
internal GitTagService(GitCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task<IReadOnlyList<GitTag>> ReadTagsAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var configuredSort = await ReadEffectiveTagSortAsync(repository, cancellationToken);
        var sort = string.IsNullOrWhiteSpace(configuredSort) ? DefaultTagSort : configuredSort;
        const string format = "%(refname)%00%(objecttype)%00%(objectname)%00%(*objectname)%00%(taggername)%00%(taggeremail)%00%(taggerdate:iso-strict)%00%(contents)%1e";
        var output = await ExecuteAsync(
            repository,
            GitCommandKind.Internal,
            cancellationToken,
            "for-each-ref",
            $"--sort={sort}",
            $"--format={format}",
            "refs/tags");

        var tags = new List<GitTag>();
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\r', '\n').Split('\0', 8);
            if (fields.Length < 8 || !fields[0].StartsWith("refs/tags/", StringComparison.Ordinal)) continue;

            var name = fields[0][10..];
            var annotated = string.Equals(fields[1], "tag", StringComparison.Ordinal);
            var objectId = fields[2];
            var targetCommit = annotated ? fields[3] : objectId;
            if (string.IsNullOrWhiteSpace(targetCommit))
                throw new InvalidOperationException($"Tag '{name}' does not peel to a commit.");

            DateTimeOffset? taggedAt = null;
            if (annotated && DateTimeOffset.TryParse(
                    fields[6],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedDate))
                taggedAt = parsedDate;

            tags.Add(new GitTag(
                name,
                targetCommit,
                annotated ? GitTagKind.Annotated : GitTagKind.Lightweight,
                annotated ? objectId : null,
                annotated ? EmptyToNull(fields[4]) : null,
                annotated ? NormalizeEmail(fields[5]) : null,
                taggedAt,
                annotated ? fields[7].TrimEnd('\r', '\n') : null));
        }

        return tags;
    }

    public async Task CreateTagAsync(
        Repository repository,
        CreateTagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Name.Trim();
        await ValidateTagNameAsync(repository, name, cancellationToken);
        if (request.Kind == GitTagKind.Annotated && string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("An annotated tag requires a non-empty message.", nameof(request));

        var existing = await ExecuteForResultAsync(
            repository,
            GitCommandKind.Internal,
            cancellationToken,
            "show-ref",
            "--verify",
            "--quiet",
            $"refs/tags/{name}");
        if (existing.ExitCode == 0)
            throw new InvalidOperationException($"Tag '{name}' already exists. CSharpGit does not move existing tags during creation.");
        if (existing.ExitCode != 1)
            ThrowGitFailure(existing);

        var target = request.TargetCommit.Trim();
        var resolved = await ExecuteForResultAsync(
            repository,
            GitCommandKind.Internal,
            cancellationToken,
            "rev-parse",
            "--verify",
            "--end-of-options",
            $"{target}^{{commit}}");
        if (resolved.ExitCode != 0 || string.IsNullOrWhiteSpace(resolved.StandardOutput))
            throw new InvalidOperationException($"Target '{request.TargetCommit}' does not exist or is not a commit. {GitDetail(resolved)}");

        if (request.Kind == GitTagKind.Annotated)
        {
            await ExecuteAsync(
                repository,
                GitCommandKind.User,
                cancellationToken,
                "tag",
                "-a",
                "-m",
                request.Message!.Trim(),
                "--",
                name,
                resolved.StandardOutput.Trim());
            return;
        }

        await ExecuteAsync(
            repository,
            GitCommandKind.User,
            cancellationToken,
            "tag",
            "--",
            name,
            resolved.StandardOutput.Trim());
    }

    public async Task DeleteTagAsync(
        Repository repository,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        var name = await ValidateTagNameAsync(repository, tagName, cancellationToken);
        await ExecuteAsync(repository, GitCommandKind.User, cancellationToken, "tag", "--delete", "--", name);
    }

    public async Task FetchTagsAsync(
        Repository repository,
        string remote,
        CancellationToken cancellationToken = default)
    {
        var remoteName = NormalizeRemote(remote);
        await ExecuteAsync(repository, GitCommandKind.User, cancellationToken, "fetch", remoteName, "--tags");
    }

    public async Task<PushTagResult> PushTagAsync(
        Repository repository,
        string remote,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        var remoteName = NormalizeRemote(remote);
        var name = await ValidateTagNameAsync(repository, tagName, cancellationToken);
        var local = await ReadLocalTagAsync(repository, name, cancellationToken)
            ?? throw new InvalidOperationException($"Local tag '{name}' no longer exists.");
        var remoteTag = await ReadRemoteTagAsync(repository, remoteName, name, cancellationToken);

        if (remoteTag is not null)
        {
            if (string.Equals(remoteTag.ObjectId, local.ObjectId, StringComparison.Ordinal))
                return new PushTagResult(PushTagResultKind.AlreadyUpToDate, $"Tag '{name}' is already up to date on '{remoteName}'.");

            return new PushTagResult(
                PushTagResultKind.Conflict,
                $"Remote '{remoteName}' already contains tag '{name}' pointing to a different object.",
                new RemoteTagConflictSnapshot(
                    remoteName,
                    name,
                    remoteTag.ObjectId,
                    remoteTag.TargetCommit,
                    local.ObjectId,
                    local.TargetCommit));
        }

        await ExecuteAsync(
            repository,
            GitCommandKind.User,
            cancellationToken,
            "push",
            "--porcelain",
            remoteName,
            $"refs/tags/{name}:refs/tags/{name}");
        return new PushTagResult(PushTagResultKind.Pushed, $"Tag '{name}' was pushed to '{remoteName}'.");
    }

    public async Task PushAllTagsAsync(
        Repository repository,
        string remote,
        CancellationToken cancellationToken = default)
    {
        var remoteName = NormalizeRemote(remote);
        await ExecuteAsync(repository, GitCommandKind.User, cancellationToken, "push", "--porcelain", remoteName, "--tags");
    }

    public async Task DeleteRemoteTagAsync(
        Repository repository,
        string remote,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        var remoteName = NormalizeRemote(remote);
        var name = await ValidateTagNameAsync(repository, tagName, cancellationToken);
        await ExecuteAsync(
            repository,
            GitCommandKind.User,
            cancellationToken,
            "push",
            "--porcelain",
            remoteName,
            $":refs/tags/{name}");
    }

    public async Task<RemoteTagInfo?> ReadRemoteTagAsync(
        Repository repository,
        string remote,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var remoteName = NormalizeRemote(remote);
        var name = await ValidateTagNameAsync(repository, tagName, cancellationToken);
        var reference = $"refs/tags/{name}";
        var output = await ExecuteAsync(
            repository,
            GitCommandKind.Internal,
            cancellationToken,
            "ls-remote",
            "--tags",
            remoteName,
            reference,
            $"{reference}^{{}}");

        string? objectId = null;
        string? peeled = null;
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('\t');
            if (separator <= 0) continue;
            var hash = line[..separator];
            var refName = line[(separator + 1)..];
            if (string.Equals(refName, reference, StringComparison.Ordinal)) objectId = hash;
            else if (string.Equals(refName, $"{reference}^{{}}", StringComparison.Ordinal)) peeled = hash;
        }

        if (objectId is null) return null;
        return new RemoteTagInfo(remoteName, name, objectId, peeled ?? objectId, peeled is not null);
    }

    public async Task ForceUpdateRemoteTagAsync(
        Repository repository,
        RemoteTagConflictSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(snapshot);

        var remote = NormalizeRemote(snapshot.Remote);
        var name = await ValidateTagNameAsync(repository, snapshot.TagName, cancellationToken);
        var local = await ReadLocalTagAsync(repository, name, cancellationToken)
            ?? throw new InvalidOperationException($"Local tag '{name}' no longer exists.");
        if (!string.Equals(local.ObjectId, snapshot.NewLocalObjectId, StringComparison.Ordinal))
            throw new InvalidOperationException("The local tag changed after the force-update confirmation was prepared. Refresh and review the tag again.");

        var remoteTag = await ReadRemoteTagAsync(repository, remote, name, cancellationToken)
            ?? throw new InvalidOperationException("The remote tag changed or disappeared after confirmation. Refresh before retrying.");
        if (!string.Equals(remoteTag.ObjectId, snapshot.CurrentRemoteObjectId, StringComparison.Ordinal))
            throw new InvalidOperationException("The remote tag changed after confirmation. CSharpGit refused to overwrite the newer remote value.");

        await ExecuteAsync(
            repository,
            GitCommandKind.User,
            cancellationToken,
            "push",
            "--porcelain",
            $"--force-with-lease=refs/tags/{name}:{snapshot.CurrentRemoteObjectId}",
            remote,
            $"refs/tags/{name}:refs/tags/{name}");
    }

    private async Task<GitTag?> ReadLocalTagAsync(
        Repository repository,
        string tagName,
        CancellationToken cancellationToken)
    {
        const string format = "%(refname)%00%(objecttype)%00%(objectname)%00%(*objectname)%00%(taggername)%00%(taggeremail)%00%(taggerdate:iso-strict)%00%(contents)%1e";
        var output = await ExecuteAsync(
            repository,
            GitCommandKind.Internal,
            cancellationToken,
            "for-each-ref",
            $"--format={format}",
            $"refs/tags/{tagName}");
        if (string.IsNullOrWhiteSpace(output)) return null;

        var fields = output.TrimStart('\r', '\n').Split('\0', 8);
        if (fields.Length < 8) return null;
        var annotated = string.Equals(fields[1], "tag", StringComparison.Ordinal);
        var target = annotated ? fields[3] : fields[2];
        DateTimeOffset? taggedAt = null;
        if (annotated && DateTimeOffset.TryParse(fields[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            taggedAt = parsed;
        return new GitTag(
            tagName,
            target,
            annotated ? GitTagKind.Annotated : GitTagKind.Lightweight,
            annotated ? fields[2] : null,
            annotated ? EmptyToNull(fields[4]) : null,
            annotated ? NormalizeEmail(fields[5]) : null,
            taggedAt,
            annotated ? fields[7].TrimEnd('\x1e', '\r', '\n') : null);
    }

    private async Task<string?> ReadEffectiveTagSortAsync(Repository repository, CancellationToken cancellationToken)
    {
        var result = await ExecuteForResultAsync(repository, GitCommandKind.Internal, cancellationToken, "config", "--get", "tag.sort");
        if (result.ExitCode == 1) return null;
        if (result.ExitCode != 0) ThrowGitFailure(result);
        return EmptyToNull(result.StandardOutput.Trim());
    }

    private async Task<string> ValidateTagNameAsync(Repository repository, string tagName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var name = tagName?.Trim() ?? string.Empty;
        if (name.Length == 0) throw new ArgumentException("Tag name is required.", nameof(tagName));

        var result = await ExecuteForResultAsync(
            repository,
            GitCommandKind.Internal,
            cancellationToken,
            "check-ref-format",
            $"refs/tags/{name}");
        if (result.ExitCode != 0)
            throw new ArgumentException($"'{tagName}' is not a valid Git tag name. {GitDetail(result)}", nameof(tagName));
        return name;
    }

    private static string NormalizeRemote(string remote)
    {
        var value = remote?.Trim() ?? string.Empty;
        if (value.Length == 0) throw new ArgumentException("A remote must be selected explicitly.", nameof(remote));
        if (value.StartsWith("-", StringComparison.Ordinal) || value.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new ArgumentException("Invalid remote name.", nameof(remote));
        return value;
    }

    private Task<string> ExecuteAsync(
        Repository repository,
        GitCommandKind kind,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        _executor.ExecuteAsync(repository.WorkingDirectory, "Tags", kind, cancellationToken, arguments);

    private Task<GitCommandResult> ExecuteForResultAsync(
        Repository repository,
        GitCommandKind kind,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        _executor.ExecuteForResultAsync(repository.WorkingDirectory, "Tags", kind, cancellationToken, null, arguments);

    private static void ThrowGitFailure(GitCommandResult result) =>
        throw new InvalidOperationException($"Git exited with code {result.ExitCode}: {GitDetail(result)}");

    private static string GitDetail(GitCommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardError)) return result.StandardError.Trim();
        if (!string.IsNullOrWhiteSpace(result.StandardOutput)) return result.StandardOutput.Trim();
        return "Git returned no diagnostic message.";
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? NormalizeEmail(string value)
    {
        var email = EmptyToNull(value)?.Trim();
        if (email is null) return null;
        return email.Length >= 2 && email[0] == '<' && email[^1] == '>' ? email[1..^1] : email;
    }
}
