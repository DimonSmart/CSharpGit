using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void ThemeModesExposeExactlySystemLightAndDarkAndReflectCurrentSetting()
    {
        var settings = new FakeAppSettingsService(ApplicationThemeMode.Dark);
        using var viewModel = new SettingsViewModel(settings);

        Assert.Equal(
            new[] { ApplicationThemeMode.System, ApplicationThemeMode.Light, ApplicationThemeMode.Dark },
            viewModel.ThemeModes.Select(option => option.Mode).ToArray());
        Assert.Equal(
            new[] { "System", "Light", "Dark" },
            viewModel.ThemeModes.Select(option => option.Label).ToArray());
        Assert.Equal(ApplicationThemeMode.Dark, viewModel.SelectedThemeMode.Mode);
    }

    [Fact]
    public async Task ApplyThemeModeUsesGlobalSettingsService()
    {
        var settings = new FakeAppSettingsService(ApplicationThemeMode.System);
        using var viewModel = new SettingsViewModel(settings);
        var dark = viewModel.ThemeModes.Single(option => option.Mode == ApplicationThemeMode.Dark);

        await viewModel.ApplyThemeModeAsync(dark);

        Assert.Equal(ApplicationThemeMode.Dark, settings.ThemeMode);
        Assert.Equal(ApplicationThemeMode.Dark, viewModel.SelectedThemeMode.Mode);
    }

    [Fact]
    public void ExternalSettingsChangeSynchronizesSelectedThemeAndRaisesPropertyChanged()
    {
        var settings = new FakeAppSettingsService(ApplicationThemeMode.System);
        using var viewModel = new SettingsViewModel(settings);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        settings.ChangeThemeExternally(ApplicationThemeMode.Light);

        Assert.Equal(ApplicationThemeMode.Light, viewModel.SelectedThemeMode.Mode);
        Assert.Contains(nameof(SettingsViewModel.SelectedThemeMode), changedProperties);
    }

    [Fact]
    public void DisposeUnsubscribesFromGlobalSettingsChanges()
    {
        var settings = new FakeAppSettingsService(ApplicationThemeMode.System);
        var viewModel = new SettingsViewModel(settings);
        Assert.Equal(1, settings.ChangedSubscriberCount);

        viewModel.Dispose();
        settings.ChangeThemeExternally(ApplicationThemeMode.Dark);

        Assert.Equal(0, settings.ChangedSubscriberCount);
        Assert.Equal(ApplicationThemeMode.System, viewModel.SelectedThemeMode.Mode);
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        private EventHandler? _changed;
        private ApplicationThemeMode _themeMode;

        public FakeAppSettingsService(ApplicationThemeMode themeMode) => _themeMode = themeMode;

        public ApplicationThemeMode ThemeMode => _themeMode;
        public CommitTimeDisplayMode CommitTimeDisplayMode { get; private set; } = CommitTimeDisplayMode.Smart;
        public bool LoggingEnabled { get; private set; }
        public ApplicationLogLevel LogLevel { get; private set; } = ApplicationLogLevel.Information;
        public IReadOnlyList<RecentRepositorySettings> RecentRepositories => [];
        public int ChangedSubscriberCount { get; private set; }

        public event EventHandler? Changed
        {
            add
            {
                _changed += value;
                ChangedSubscriberCount++;
            }
            remove
            {
                _changed -= value;
                ChangedSubscriberCount--;
            }
        }

        public Task SetThemeModeAsync(
            ApplicationThemeMode mode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_themeMode == mode) return Task.CompletedTask;
            _themeMode = mode;
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetCommitTimeDisplayModeAsync(
            CommitTimeDisplayMode mode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CommitTimeDisplayMode == mode) return Task.CompletedTask;
            CommitTimeDisplayMode = mode;
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetLoggingSettingsAsync(
            bool enabled,
            ApplicationLogLevel level,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LoggingEnabled == enabled && LogLevel == level) return Task.CompletedTask;
            LoggingEnabled = enabled;
            LogLevel = level;
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task RecordRecentRepositoryAsync(
            string path,
            string displayName,
            string? lastBranchName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveRecentRepositoryAsync(
            string path,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ChangeThemeExternally(ApplicationThemeMode mode)
        {
            _themeMode = mode;
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
