using System.Globalization;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed class GitRepositoryMaintenanceService : IRepositoryMaintenanceService
{
    private readonly GitCommandExecutor _executor;

    public GitRepositoryMaintenanceService()
        : this(GitCommandExecutor.Default)
    {
    }

    internal GitRepositoryMaintenanceService(GitCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task<RepositoryStorageStatistics> GetStorageStatisticsAsync(
        Repository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var output = await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "RepositoryStorageStatistics",
            GitCommandKind.Internal,
            cancellationToken,
            "count-objects",
            "-v");

        return ParseStorageStatistics(output);
    }

    public async Task GarbageCollectAsync(
        Repository repository,
        RepositoryGcOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);

        await _executor.ExecuteAsync(
            repository.WorkingDirectory,
            "RepositoryGarbageCollect",
            GitCommandKind.User,
            cancellationToken,
            BuildGarbageCollectArguments(options));
    }

    internal static string[] BuildGarbageCollectArguments(RepositoryGcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var arguments = new List<string> { "gc" };
        if (options.Aggressive) arguments.Add("--aggressive");
        if (options.PruneNow) arguments.Add("--prune=now");
        if (options.KeepLargestPack) arguments.Add("--keep-largest-pack");
        return [.. arguments];
    }

    internal static RepositoryStorageStatistics ParseStorageStatistics(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            values[key] = value;
        }

        var looseObjects = ReadRequiredNonNegativeLong(values, "count");
        var looseSizeKiB = ReadRequiredNonNegativeLong(values, "size");
        var packedObjects = ReadRequiredNonNegativeLong(values, "in-pack");
        var packCountValue = ReadRequiredNonNegativeLong(values, "packs");
        var packedSizeKiB = ReadRequiredNonNegativeLong(values, "size-pack");

        int packCount;
        try
        {
            packCount = checked((int)packCountValue);
        }
        catch (OverflowException exception)
        {
            throw InvalidField("packs", "is outside the supported range", exception);
        }

        var looseBytes = KiBToBytes(looseSizeKiB, "size");
        var packedBytes = KiBToBytes(packedSizeKiB, "size-pack");
        try
        {
            _ = checked(looseBytes + packedBytes);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                "git count-objects reported object storage sizes whose sum is too large.",
                exception);
        }

        return new RepositoryStorageStatistics(
            looseObjects,
            packedObjects,
            packCount,
            looseBytes,
            packedBytes);
    }

    private static long ReadRequiredNonNegativeLong(
        IReadOnlyDictionary<string, string> values,
        string field)
    {
        if (!values.TryGetValue(field, out var text))
            throw InvalidField(field, "is missing");

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw InvalidField(field, "is not a valid integer");

        if (value < 0)
            throw InvalidField(field, "must not be negative");

        return value;
    }

    private static long KiBToBytes(long value, string field)
    {
        try
        {
            return checked(value * 1024L);
        }
        catch (OverflowException exception)
        {
            throw InvalidField(field, "is too large to convert from KiB to bytes", exception);
        }
    }

    private static InvalidOperationException InvalidField(
        string field,
        string problem,
        Exception? innerException = null) =>
        new($"Invalid git count-objects output: field '{field}' {problem}.", innerException);
}
