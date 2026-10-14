using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace CSharpGit.Infrastructure;

public sealed partial class RepositoryImageService
{
    private static bool CandidateMatchesMetadata(FileInfo? candidate, RepositoryImageCacheMetadata metadata)
    {
        if (candidate is null) return metadata.SourcePath is null;
        return PathsEqual(candidate.FullName, metadata.SourcePath)
            && candidate.Length == metadata.SourceLength
            && new DateTimeOffset(candidate.LastWriteTimeUtc, TimeSpan.Zero) == metadata.SourceLastWriteUtc;
    }

    private static void AtomicWriteText(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, value);
            File.Move(temp, path, true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void AtomicWriteBytes(string path, byte[] value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, value);
            File.Move(temp, path, true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static string NormalizeRepositoryPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string NormalizePathIdentity(string path)
    {
        var normalized = NormalizeRepositoryPath(path);
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? normalized.ToUpperInvariant()
            : normalized;
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (left is null || right is null) return left is null && right is null;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }

    private static string Hash(string value) => HashBytes(Encoding.UTF8.GetBytes(value.Trim()));
    private static string HashBytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);

    private static bool IsCachedImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractKey(string fileName)
    {
        var separator = fileName.IndexOf('-');
        if (separator != 64) return null;
        var key = fileName[..separator];
        return IsHash(key) ? key : null;
    }

    private static void Touch(string path, DateTimeOffset time)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, time.UtcDateTime);
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CSharpGit", "0.1"));
        return client;
    }

    private static void Log(string message, Exception? exception = null)
    {
        if (exception is null)
            Trace.TraceWarning("[RepositoryImage] {0}", message);
        else
            Trace.TraceWarning(
                "[RepositoryImage] {0} {1}: {2}",
                message,
                exception.GetType().Name,
                exception.Message);
    }
}
