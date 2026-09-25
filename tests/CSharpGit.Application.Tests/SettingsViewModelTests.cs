using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Threading;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task BackgroundChangedIsDispatchedAndSynchronizesEveryPresentedSetting()
    {
        var settings = new FakeAppSettingsService();
        var dispatcher = new TestUiDispatcher(hasThreadAccess: false);
        using var viewModel = new SettingsViewModel(settings, dispatcher);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        await settings.ChangeExternallyOnBackgroundAsync(
            ApplicationThemeMode.Dark,
            CommitTimeDisplayMode.Absolute,
            true,
            ApplicationLogLevel.Trace,
            GitConsoleAutoOpenMode.Always,
            true);

        Assert.Empty(changedProperties);
        Assert.Equal(ApplicationThemeMode.System, viewModel.SelectedThemeMode.Mode);
        Assert.Single(dispatcher.QueuedActions);

        dispatcher.RunAll();

        Assert.Equal(ApplicationThemeMode.Dark, viewModel.SelectedThemeMode.Mode);
        Assert.Equal(CommitTimeDisplayMode.Absolute, viewModel.SelectedCommitTimeMode.Mode);
        Assert.True(viewModel.LoggingEnabled);
        Assert.Equal(ApplicationLogLevel.Trace, viewModel.SelectedLogLevel.Level);
        Assert.Equal(GitConsoleAutoOpenMode.Always, viewModel.SelectedGitConsoleAutoOpenMode.Mode);
        Assert.True(viewModel.AutoSetupRemoteOnPush);
        Assert.True(viewModel.ShowAuthorAvatars);
        Assert.True(viewModel.OnlineAvatarLookupEnabled);
        Assert.Contains(nameof(SettingsViewModel.SelectedCommitTimeMode), changedProperties);
        Assert.Contains(nameof(SettingsViewModel.LoggingEnabled), changedProperties);
        Assert.Contains(nameof(SettingsViewModel.SelectedLogLevel), changedProperties);
    }

    [Fact]
    public async Task QueuedSettingsCallbackAfterDisposeIsNoOp()
    {
        var settings = new FakeAppSettingsService();
        var dispatcher = new TestUiDispatcher(hasThreadAccess: false);
        var viewModel = new SettingsViewModel(settings, dispatcher);
        var changes = 0;
        viewModel.PropertyChanged += (_, _) => changes++;

        await settings.ChangeExternallyOnBackgroundAsync(
            ApplicationThemeMode.Dark,
            CommitTimeDisplayMode.Absolute,
            true,
            ApplicationLogLevel.Trace,
            GitConsoleAutoOpenMode.Always,
            true);
        Assert.Single(dispatcher.QueuedActions);

        viewModel.Dispose();
        dispatcher.RunAll();

        Assert.Equal(0, changes);
        Assert.Equal(ApplicationThemeMode.System, viewModel.SelectedThemeMode.Mode);
        Assert.Equal(0, settings.ChangedSubscriberCount);
    }

    [Fact]
    public async Task ThemePersistenceFailureRestoresCommittedState()
    {
        var settings = new FakeAppSettingsService { ThemeMode = ApplicationThemeMode.Light };
        using var viewModel = CreateViewModel(settings);
        var dark = viewModel.ThemeModes.Single(option => option.Mode == ApplicationThemeMode.Dark);
        settings.FailNextWrite = true;

        await Assert.ThrowsAsync<IOException>(() => viewModel.ApplyThemeModeAsync(dark));

        Assert.Equal(ApplicationThemeMode.Light, settings.ThemeMode);
        Assert.Equal(ApplicationThemeMode.Light, viewModel.SelectedThemeMode.Mode);
        Assert.Equal(1, settings.ThemeWriteAttempts);
    }

    [Fact]
    public async Task CommitTimePersistenceFailureRestoresCommittedState()
    {
        var settings = new FakeAppSettingsService { CommitTimeDisplayMode = CommitTimeDisplayMode.Smart };
        using var viewModel = CreateViewModel(settings);
        var absolute = viewModel.CommitTimeModes.Single(option => option.Mode == CommitTimeDisplayMode.Absolute);
        settings.FailNextWrite = true;

        await Assert.ThrowsAsync<IOException>(() => viewModel.ApplyCommitTimeModeAsync(absolute));

        Assert.Equal(CommitTimeDisplayMode.Smart, settings.CommitTimeDisplayMode);
        Assert.Equal(CommitTimeDisplayMode.Smart, viewModel.SelectedCommitTimeMode.Mode);
    }

    [Fact]
    public async Task LoggingPersistenceFailureRestoresCommittedPair()
    {
        var settings = new FakeAppSettingsService();
        using var viewModel = CreateViewModel(settings);
        var trace = viewModel.LogLevels.Single(option => option.Level == ApplicationLogLevel.Trace);
        settings.FailNextWrite = true;

        await Assert.ThrowsAsync<IOException>(() => viewModel.ApplyLoggingSettingsAsync(true, trace));

        Assert.False(settings.LoggingEnabled);
        Assert.Equal(ApplicationLogLevel.Information, settings.LogLevel);
        Assert.False(viewModel.LoggingEnabled);
        Assert.Equal(ApplicationLogLevel.Information, viewModel.SelectedLogLevel.Level);
    }

    [Fact]
    public async Task GitConsoleAndAutoSetupFailuresRestoreCommittedState()
    {
        var settings = new FakeAppSettingsService();
        using var viewModel = CreateViewModel(settings);
        var always = viewModel.GitConsoleAutoOpenModes.Single(option => option.Mode == GitConsoleAutoOpenMode.Always);

        settings.FailNextWrite = true;
        await Assert.ThrowsAsync<IOException>(() => viewModel.ApplyGitConsoleAutoOpenModeAsync(always));
        Assert.Equal(GitConsoleAutoOpenMode.OnErrors, viewModel.SelectedGitConsoleAutoOpenMode.Mode);

        settings.FailNextWrite = true;
        await Assert.ThrowsAsync<IOException>(() => viewModel.ApplyAutoSetupRemoteOnPushAsync(true));
        Assert.False(viewModel.AutoSetupRemoteOnPush);
    }

    [Fact]
    public async Task AvatarPersistenceFailureRestoresCommittedState()
    {
        var settings = new FakeAppSettingsService
        {
            ShowAuthorAvatars = true,
            OnlineAvatarLookupEnabled = true
        };
        using var viewModel = CreateViewModel(settings);

        settings.FailNextWrite = true;
        await Assert.ThrowsAsync<IOException>(() => viewModel.ApplyShowAuthorAvatarsAsync(false));
        Assert.True(viewModel.ShowAuthorAvatars);

        settings.FailNextWrite = true;
        await Assert.ThrowsAsync<IOException>(() => viewModel.ApplyOnlineAvatarLookupEnabledAsync(false));
        Assert.True(viewModel.OnlineAvatarLookupEnabled);
    }

    [Fact]
    public async Task RollbackPublishesUnderSynchronizationSuppressionAndDoesNotCauseSecondWrite()
    {
        var settings = new FakeAppSettingsService { ThemeMode = ApplicationThemeMode.Light };
        using var viewModel = CreateViewModel(settings);
        var dark = viewModel.ThemeModes.Single(option => option.Mode == ApplicationThemeMode.Dark);
        var rollbackWasSuppressed = false;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.SelectedThemeMode) &&
                viewModel.SelectedThemeMode.Mode == ApplicationThemeMode.Light)
                rollbackWasSuppressed = viewModel.IsSynchronizingFromSettings;
        };

        settings.FailNextWrite = true;
        await Assert.ThrowsAsync<IOException>(() => viewModel.ApplyThemeModeAsync(dark));

        Assert.True(rollbackWasSuppressed);
        Assert.Equal(1, settings.ThemeWriteAttempts);
    }

    [Fact]
    public async Task HistoryPerformanceDiagnosticsAppliesWithoutRestart()
    {
        var settings = new FakeAppSettingsService();
        using var viewModel = CreateViewModel(settings);

        await viewModel.ApplyHistoryPerformanceDiagnosticsEnabledAsync(true);

        Assert.True(settings.HistoryPerformanceDiagnosticsEnabled);
        Assert.True(viewModel.HistoryPerformanceDiagnosticsEnabled);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var settings = new FakeAppSettingsService();
        var viewModel = CreateViewModel(settings);
        Assert.Equal(1, settings.ChangedSubscriberCount);

        viewModel.Dispose();
        viewModel.Dispose();

        Assert.Equal(0, settings.ChangedSubscriberCount);
    }

    private static SettingsViewModel CreateViewModel(FakeAppSettingsService settings) =>
        new(settings, new TestUiDispatcher(hasThreadAccess: true));

    private sealed class TestUiDispatcher(bool hasThreadAccess) : IUiDispatcher
    {
        private readonly Queue<Action> _queuedActions = new();

        public bool HasThreadAccess { get; set; } = hasThreadAccess;
        public bool RejectEnqueue { get; set; }
        public IReadOnlyCollection<Action> QueuedActions => _queuedActions;

        public bool TryEnqueue(Action action)
        {
            if (RejectEnqueue) return false;
            _queuedActions.Enqueue(action);
            return true;
        }

        public void RunAll()
        {
            while (_queuedActions.TryDequeue(out var action))
                action();
        }
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        private EventHandler? _changed;

        public ApplicationThemeMode ThemeMode { get; set; } = ApplicationThemeMode.System;
        public CommitTimeDisplayMode CommitTimeDisplayMode { get; set; } = CommitTimeDisplayMode.Smart;
        public bool LoggingEnabled { get; set; }
        public ApplicationLogLevel LogLevel { get; set; } = ApplicationLogLevel.Information;
        public GitConsoleAutoOpenMode GitConsoleAutoOpenMode { get; set; } = GitConsoleAutoOpenMode.OnErrors;
        public bool ShowReflog { get; set; }
        public bool AutoSetupRemoteOnPush { get; set; }
        public bool ShowAuthorAvatars { get; set; } = true;
        public bool OnlineAvatarLookupEnabled { get; set; } = true;
        public bool HistoryPerformanceDiagnosticsEnabled { get; set; }
        public IReadOnlyList<RecentRepositorySettings> RecentRepositories => [];
        public bool FailNextWrite { get; set; }
        public int ChangedSubscriberCount { get; private set; }
        public int ThemeWriteAttempts { get; private set; }

        public event EventHandler? Changed
        {
            add { _changed += value; ChangedSubscriberCount++; }
            remove { _changed -= value; ChangedSubscriberCount--; }
        }

        public Task SetThemeModeAsync(ApplicationThemeMode mode, CancellationToken cancellationToken = default)
        {
            ThemeWriteAttempts++;
            ThrowIfWriteFails(cancellationToken);
            if (ThemeMode == mode) return Task.CompletedTask;
            ThemeMode = mode;
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetCommitTimeDisplayModeAsync(CommitTimeDisplayMode mode, CancellationToken cancellationToken = default)
        {
            ThrowIfWriteFails(cancellationToken);
            if (CommitTimeDisplayMode == mode) return Task.CompletedTask;
            CommitTimeDisplayMode = mode;
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetLoggingSettingsAsync(bool enabled, ApplicationLogLevel level, CancellationToken cancellationToken = default)
        {
            ThrowIfWriteFails(cancellationToken);
            if (LoggingEnabled == enabled && LogLevel == level) return Task.CompletedTask;
            LoggingEnabled = enabled;
            LogLevel = level;
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetGitConsoleAutoOpenModeAsync(GitConsoleAutoOpenMode mode, CancellationToken cancellationToken = default)
        {
            ThrowIfWriteFails(cancellationToken);
            if (GitConsoleAutoOpenMode == mode) return Task.CompletedTask;
            GitConsoleAutoOpenMode = mode;
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetShowReflogAsync(bool value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShowReflog = value;
            return Task.CompletedTask;
        }

        public Task SetAutoSetupRemoteOnPushAsync(bool value, CancellationToken cancellationToken = default)
        {
            ThrowIfWriteFails(cancellationToken);
            if (AutoSetupRemoteOnPush == value) return Task.CompletedTask;
            AutoSetupRemoteOnPush = value;
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetShowAuthorAvatarsAsync(bool value, CancellationToken cancellationToken = default)
        {
            ThrowIfWriteFails(cancellationToken);
            if (ShowAuthorAvatars == value) return Task.CompletedTask;
            ShowAuthorAvatars = value;
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetOnlineAvatarLookupEnabledAsync(bool value, CancellationToken cancellationToken = default)
        {
            ThrowIfWriteFails(cancellationToken);
            if (OnlineAvatarLookupEnabled == value) return Task.CompletedTask;
            OnlineAvatarLookupEnabled = value;
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task SetHistoryPerformanceDiagnosticsEnabledAsync(bool value, CancellationToken cancellationToken = default)
        {
            ThrowIfWriteFails(cancellationToken);
            if (HistoryPerformanceDiagnosticsEnabled == value) return Task.CompletedTask;
            HistoryPerformanceDiagnosticsEnabled = value;
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task RecordRecentRepositoryAsync(string path, string displayName, string? lastBranchName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveRecentRepositoryAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ChangeExternallyOnBackgroundAsync(
            ApplicationThemeMode theme,
            CommitTimeDisplayMode commitTime,
            bool loggingEnabled,
            ApplicationLogLevel logLevel,
            GitConsoleAutoOpenMode gitConsoleMode,
            bool autoSetupRemoteOnPush,
            bool showAuthorAvatars = true,
            bool onlineAvatarLookupEnabled = true) =>
            Task.Run(() =>
            {
                ThemeMode = theme;
                CommitTimeDisplayMode = commitTime;
                LoggingEnabled = loggingEnabled;
                LogLevel = logLevel;
                GitConsoleAutoOpenMode = gitConsoleMode;
                AutoSetupRemoteOnPush = autoSetupRemoteOnPush;
                ShowAuthorAvatars = showAuthorAvatars;
                OnlineAvatarLookupEnabled = onlineAvatarLookupEnabled;
                _changed?.Invoke(this, EventArgs.Empty);
            });

        private void ThrowIfWriteFails(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FailNextWrite) return;
            FailNextWrite = false;
            throw new IOException("Simulated persistence failure.");
        }
    }
}
