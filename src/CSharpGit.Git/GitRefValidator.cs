namespace CSharpGit.Git;

internal static class GitRefValidator
{
    internal static void Validate(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith('-')
            || value.Contains('\0')
            || value.Any(char.IsWhiteSpace))
            throw new ArgumentException("Invalid Git ref name.", parameterName);
    }

    internal static void ValidateObjectId(string value, string parameterName = "hash")
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Invalid commit hash.", parameterName);
    }
}
