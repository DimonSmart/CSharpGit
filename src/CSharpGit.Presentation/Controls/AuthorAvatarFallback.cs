using System.Security.Cryptography;
using System.Text;

namespace CSharpGit.Presentation.Controls;

internal static class AuthorAvatarFallback
{
    public static string GetInitials(string? authorName, string? authorEmail)
    {
        var words = (authorName ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 1)
            return FirstTextElement(words[0]);
        if (words.Length > 1)
            return FirstTextElement(words[0]) + FirstTextElement(words[^1]);

        var email = authorEmail?.Trim();
        if (!string.IsNullOrEmpty(email))
        {
            var at = email.IndexOf('@');
            var local = at > 0 ? email[..at] : email;
            if (local.Length > 0)
                return FirstTextElement(local);
        }

        return "?";
    }

    public static int GetStableColorIndex(
        string? authorName,
        string? authorEmail,
        int paletteSize)
    {
        if (paletteSize <= 0) throw new ArgumentOutOfRangeException(nameof(paletteSize));

        var identity = !string.IsNullOrWhiteSpace(authorEmail)
            ? authorEmail.Trim().ToLowerInvariant()
            : (authorName ?? string.Empty).Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return bytes[0] % paletteSize;
    }

    private static string FirstTextElement(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var codePoint = char.ConvertToUtf32(value, 0);
        return char.ConvertFromUtf32(codePoint).ToUpperInvariant();
    }
}
