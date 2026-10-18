using System.Text.Json;
using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

public sealed class GitCommandFeedbackSettingsTests
{
    [Fact]
    public async Task OptionalSettingsDefaultFalseAndArePersistedWithoutChangingConsolePolicy()
    {
        var folder = Path.Combine(Path.GetTempPath(), "csharpgit-feedback-settings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, """{"GitConsoleAutoOpenMode":"Never"}""");
            var service = new JsonAppSettingsService(path);
            Assert.False(service.ShowGitCommandProgressDialog);
            Assert.False(service.AutoCloseSuccessfulGitCommandFeedback);
            Assert.Equal(GitConsoleAutoOpenMode.Never, service.GitConsoleAutoOpenMode);

            var notifications = 0;
            service.Changed += (_, _) => Interlocked.Increment(ref notifications);
            await service.SetShowGitCommandProgressDialogAsync(true);
            await service.SetAutoCloseSuccessfulGitCommandFeedbackAsync(true);
            await service.SetShowGitCommandProgressDialogAsync(true);
            Assert.Equal(2, notifications);

            var restored = new JsonAppSettingsService(path);
            Assert.True(restored.ShowGitCommandProgressDialog);
            Assert.True(restored.AutoCloseSuccessfulGitCommandFeedback);
            Assert.Equal(GitConsoleAutoOpenMode.Never, restored.GitConsoleAutoOpenMode);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(json.RootElement.GetProperty("ShowGitCommandProgressDialog").GetBoolean());
            Assert.True(json.RootElement.GetProperty("AutoCloseSuccessfulGitCommandFeedback").GetBoolean());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
