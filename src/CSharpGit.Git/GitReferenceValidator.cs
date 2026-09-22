namespace CSharpGit.Git;

internal static class GitReferenceValidator
{
    internal static void ValidateRefName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith('-')
            || value.Contains('\0')
            || value.Any(char.IsWhiteSpace))
            throw new ArgumentException("Invalid Git ref name.", parameterName);
    }

    internal static void ValidateObjectName(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)
            || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Invalid commit hash.", nameof(hash));
    }
}
