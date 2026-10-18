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
            Assert.Equal(5, service.SuccessfulGitCommandFeedbackDurationSeconds);
            Assert.Equal(GitConsoleAutoOpenMode.Never, service.GitConsoleAutoOpenMode);

            var notifications = 0;
            service.Changed += (_, _) => Interlocked.Increment(ref notifications);
            await service.SetShowGitCommandProgressDialogAsync(true);
            await service.SetAutoCloseSuccessfulGitCommandFeedbackAsync(true);
            await service.SetSuccessfulGitCommandFeedbackDurationSecondsAsync(30);
            await service.SetShowGitCommandProgressDialogAsync(true);
            Assert.Equal(3, notifications);

            var restored = new JsonAppSettingsService(path);
            Assert.True(restored.ShowGitCommandProgressDialog);
            Assert.True(restored.AutoCloseSuccessfulGitCommandFeedback);
            Assert.Equal(30, restored.SuccessfulGitCommandFeedbackDurationSeconds);
            Assert.Equal(GitConsoleAutoOpenMode.Never, restored.GitConsoleAutoOpenMode);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(json.RootElement.GetProperty("ShowGitCommandProgressDialog").GetBoolean());
            Assert.True(json.RootElement.GetProperty("AutoCloseSuccessfulGitCommandFeedback").GetBoolean());
            Assert.Equal(30, json.RootElement.GetProperty("SuccessfulGitCommandFeedbackDurationSeconds").GetInt32());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(31)]
    public async Task RejectsOutOfRangeDurations(int seconds)
    {
        var path = Path.Combine(Path.GetTempPath(), "csharpgit-feedback-settings", Guid.NewGuid() + ".json");
        var settings = new JsonAppSettingsService(path);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => settings.SetSuccessfulGitCommandFeedbackDurationSecondsAsync(seconds));
        Assert.Equal(5, settings.SuccessfulGitCommandFeedbackDurationSeconds);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SuccessfulGitCommandFeedbackDurationSeconds\":-2}")]
    [InlineData("{\"SuccessfulGitCommandFeedbackDurationSeconds\":31}")]
    public void MissingOrInvalidDurationUsesFiveSeconds(string json)
    {
        var folder = Path.Combine(Path.GetTempPath(), "csharpgit-feedback-settings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "settings.json");
            File.WriteAllText(file, json);
            Assert.Equal(5, new JsonAppSettingsService(file).SuccessfulGitCommandFeedbackDurationSeconds);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
