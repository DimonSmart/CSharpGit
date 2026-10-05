namespace CSharpGit.Git;

internal static class GitIdentityValidation
{
    internal static string? NormalizeOptionalName(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
            return null;

        EnsureSingleLine(normalized, "Name");
        return normalized;
    }

    internal static string? NormalizeOptionalEmail(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
            return null;

        ValidateEmail(normalized);
        return normalized;
    }

    internal static string NormalizeRequiredAuthorName(string? value)
    {
        var normalized = NormalizeOptionalName(value)
            ?? throw new ArgumentException("Author name is required.", nameof(value));

        EnsureNoAuthorDelimiters(normalized, "Author name");
        return normalized;
    }

    internal static string NormalizeRequiredAuthorEmail(string? value)
    {
        var normalized = NormalizeOptionalEmail(value)
            ?? throw new ArgumentException("Author email is required.", nameof(value));

        EnsureNoAuthorDelimiters(normalized, "Author email");
        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static void ValidateEmail(string value)
    {
        EnsureSingleLine(value, "Email");

        if (value.Any(char.IsWhiteSpace))
            throw new ArgumentException("Email cannot contain whitespace.");

        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
            throw new ArgumentException("Enter an email with non-empty parts before and after @.");
    }

    private static void EnsureSingleLine(string value, string field)
    {
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException($"{field} must be a single-line value.");
    }

    private static void EnsureNoAuthorDelimiters(string value, string field)
    {
        if (value.IndexOfAny(['<', '>']) >= 0)
            throw new ArgumentException($"{field} cannot contain < or >.");
    }
}
