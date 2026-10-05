namespace CSharpGit.Domain;

public enum RepositoryIdentityField
{
    Name,
    Email
}

public sealed record GitIdentityValue(
    string? EffectiveValue,
    GitConfigSource EffectiveSource,
    string? EffectiveOrigin,
    string? RepositoryValue,
    bool HasRepositoryOverride);

public sealed record RepositoryIdentitySnapshot(
    GitIdentityValue Name,
    GitIdentityValue Email);

public sealed record RepositoryIdentityEdit(
    bool UpdateName,
    string? Name,
    bool UpdateEmail,
    string? Email);
