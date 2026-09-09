using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Infrastructure;

public sealed class JsonAppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private CommitTimeDisplayMode _commitTimeDisplayMode;

    public JsonAppSettingsService()
        : this(GetDefaultFilePath())
    {
    }

    internal JsonAppSettingsService(string filePath)
    {
        _filePath = filePath;
        _commitTimeDisplayMode = LoadCommitTimeDisplayMode();
    }

    public CommitTimeDisplayMode CommitTimeDisplayMode => _commitTimeDisplayMode;

    public event EventHandler? Changed;

    public async Task SetCommitTimeDisplayModeAsync(
        CommitTimeDisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(CommitTimeDisplayMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (_commitTimeDisplayMode == mode) return;

        _commitTimeDisplayMode = mode;
        Changed?.Invoke(this, EventArgs.Empty);
        await PersistAsync(mode, cancellationToken);
    }

    private CommitTimeDisplayMode LoadCommitTimeDisplayMode()
    {
        try
        {
            if (!File.Exists(_filePath)) return CommitTimeDisplayMode.Smart;
            var document = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(_filePath), SerializerOptions);
            return document is not null && Enum.IsDefined(typeof(CommitTimeDisplayMode), document.CommitTimeDisplayMode)
                ? document.CommitTimeDisplayMode
                : CommitTimeDisplayMode.Smart;
        }
        catch (JsonException)
        {
            return CommitTimeDisplayMode.Smart;
        }
        catch (IOException)
        {
            return CommitTimeDisplayMode.Smart;
        }
        catch (UnauthorizedAccessException)
        {
            return CommitTimeDisplayMode.Smart;
        }
    }

    private async Task PersistAsync(CommitTimeDisplayMode mode, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(new SettingsDocument(mode), SerializerOptions);
            var temporaryPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static string GetDefaultFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
        return Path.Combine(root, "CSharpGit.Presentation", "settings.json");
    }

    private sealed record SettingsDocument(CommitTimeDisplayMode CommitTimeDisplayMode);
}
