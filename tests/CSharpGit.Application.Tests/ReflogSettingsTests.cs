using System.Text.Json;
using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

public sealed class ReflogSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "csharpgit-reflog-settings-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingAndLegacySettingsDefaultToReflogOff()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        Assert.False(new JsonAppSettingsService(path).ShowReflog);

        File.WriteAllText(path, "{ \"CommitTimeDisplayMode\": \"Absolute\" }");
        Assert.False(new JsonAppSettingsService(path).ShowReflog);
    }

    [Fact]
    public async Task ShowReflogIsPersistedAndRestored()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        var service = new JsonAppSettingsService(path);

        await service.SetShowReflogAsync(true);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.True(document.RootElement.GetProperty("ShowReflog").GetBoolean());
        Assert.True(new JsonAppSettingsService(path).ShowReflog);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
