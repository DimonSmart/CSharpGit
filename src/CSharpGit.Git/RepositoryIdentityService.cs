using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

internal sealed class RepositoryIdentityService : IRepositoryIdentityService
{
    private const string NameKey = "user.name";
    private const string EmailKey = "user.email";
    private readonly GitConfigService _config;

    internal RepositoryIdentityService(GitConfigService config) =>
        _config = config ?? throw new ArgumentNullException(nameof(config));

    public async Task<RepositoryIdentitySnapshot> ReadAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var nameEffective = await _config.ReadEffectiveAsync(repository, NameKey, cancellationToken);
        var emailEffective = await _config.ReadEffectiveAsync(repository, EmailKey, cancellationToken);
        var nameDirect = await _config.ReadDirectScopeAsync(repository, NameKey, GitConfigScope.Repository, cancellationToken);
        var emailDirect = await _config.ReadDirectScopeAsync(repository, EmailKey, GitConfigScope.Repository, cancellationToken);

        return new RepositoryIdentitySnapshot(
            ToIdentityValue(nameEffective, nameDirect),
            ToIdentityValue(emailEffective, emailDirect));
    }

    public async Task SaveAsync(
        Repository repository,
        RepositoryIdentityEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(edit);

        if (edit.UpdateName)
            await SaveValueAsync(repository, NameKey, NormalizeName(edit.Name), cancellationToken);

        if (edit.UpdateEmail)
            await SaveValueAsync(repository, EmailKey, NormalizeEmail(edit.Email), cancellationToken);
    }

    public Task RemoveOverrideAsync(
        Repository repository,
        RepositoryIdentityField field,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var key = field switch
        {
            RepositoryIdentityField.Name => NameKey,
            RepositoryIdentityField.Email => EmailKey,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        return _config.UnsetAllAsync(repository, GitConfigScope.Repository, key, cancellationToken);
    }

    private async Task SaveValueAsync(
        Repository repository,
        string key,
        string? value,
        CancellationToken cancellationToken)
    {
        var existing = await _config.ReadDirectValuesAsync(repository, key, GitConfigScope.Repository, cancellationToken);
        if (value is null)
        {
            if (existing.Count == 0) return;
            await _config.UnsetAllAsync(repository, GitConfigScope.Repository, key, cancellationToken);
            return;
        }

        if (existing.Count == 1 && string.Equals(existing[0].Value, value, StringComparison.Ordinal))
            return;

        await _config.SetValueAsync(
            repository,
            GitConfigScope.Repository,
            key,
            value,
            replaceAll: true,
            cancellationToken);
    }

    private static GitIdentityValue ToIdentityValue(GitConfigValue? effective, GitConfigValue? direct) =>
        new(
            effective?.Value,
            effective?.Source ?? GitConfigSource.NotConfigured,
            effective?.Origin,
            direct?.Value,
            direct is not null);

    private static string? NormalizeName(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null) return null;
        EnsureSingleLine(normalized, "Name");
        return normalized;
    }

    private static string? NormalizeEmail(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null) return null;
        EnsureSingleLine(normalized, "Email");

        if (normalized.Any(char.IsWhiteSpace))
            throw new ArgumentException("Email cannot contain whitespace.", nameof(value));

        var at = normalized.IndexOf('@');
        if (at <= 0 || at != normalized.LastIndexOf('@') || at == normalized.Length - 1)
            throw new ArgumentException("Enter an email with non-empty parts before and after @.", nameof(value));

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static void EnsureSingleLine(string value, string field)
    {
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException($"{field} must be a single-line value.");
    }
}
