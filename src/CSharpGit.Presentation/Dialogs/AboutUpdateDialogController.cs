using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Dialogs;

// One instance per window. GitHub/Homebrew work stays behind application services.
internal sealed class AboutUpdateDialogController(
    IApplicationVersionProvider applicationVersionProvider,
    IUpdateCheckService updateCheckService,
    IApplicationUpdateInstaller applicationUpdateInstaller,
    ISystemUriLauncher systemUriLauncher,
    ApplicationUpdateActivityState applicationUpdateActivity,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Action<Func<Task>, string> runUiOperation,
    Action<Exception, string> reportUiFailure,
    Func<bool> isShuttingDown,
    Func<bool> isHistoryRewriteInProgress,
    Func<bool> isRepositoryMaintenanceInProgress,
    Func<bool> hasUnappliedCommitMessage,
    Func<Task<bool>> confirmCloseAsync,
    Action completeRestart)
{
    private readonly IApplicationVersionProvider _applicationVersionProvider = applicationVersionProvider;
    private readonly IUpdateCheckService _updateCheckService = updateCheckService;
    private readonly IApplicationUpdateInstaller _applicationUpdateInstaller = applicationUpdateInstaller;
    private readonly ISystemUriLauncher _systemUriLauncher = systemUriLauncher;
    private readonly ApplicationUpdateActivityState _applicationUpdateActivity = applicationUpdateActivity;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Action<Func<Task>, string> _runUiOperation = runUiOperation;
    private readonly Action<Exception, string> _reportUiFailure = reportUiFailure;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private readonly Func<bool> _isHistoryRewriteInProgress = isHistoryRewriteInProgress;
    private readonly Func<bool> _isRepositoryMaintenanceInProgress = isRepositoryMaintenanceInProgress;
    private readonly Func<bool> _hasUnappliedCommitMessage = hasUnappliedCommitMessage;
    private readonly Func<Task<bool>> _confirmCloseAsync = confirmCloseAsync;
    private readonly Action _completeRestart = completeRestart;

    internal bool IsRunning => _applicationUpdateActivity.IsRunning;

    internal async Task ShowAboutAsync()
    {
        using var cancellation = new CancellationTokenSource();
        var dialogLifetime = cancellation.Token;

        var statusText = new TextBlock
        {
            Text = "Checking for updates...",
            TextWrapping = TextWrapping.Wrap
        };
        var updateButton = new Button
        { Style = UiStyles.Resolve<Style>("CompactButtonStyle"),
            Content = "Update now",
            Visibility = Visibility.Collapsed
        };
        var releaseButton = new Button
        { Style = UiStyles.Resolve<Style>("CompactButtonStyle"),
            Content = "Open release page",
            Visibility = Visibility.Collapsed
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        actions.Children.Add(updateButton);
        actions.Children.Add(releaseButton);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "CSharpGit", FontSize = 22 });
        content.Children.Add(new TextBlock { Text = $"Version: {_applicationVersionProvider.DisplayVersion}" });
        content.Children.Add(statusText);
        content.Children.Add(actions);

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "About CSharpGit",
            Content = content,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        Uri? releasePageUri = null;
        ReleaseVersion? availableVersion = null;

        releaseButton.Click += (_, _) =>
            _runUiOperation(OpenReleasePageAsync, "Open release page");

        async Task OpenReleasePageAsync()
        {
            var uri = releasePageUri;
            if (uri is null || dialogLifetime.IsCancellationRequested || _isShuttingDown()) return;
            try { await _systemUriLauncher.OpenUriAsync(uri); }
            catch (Exception exception)
            {
                _reportUiFailure(exception, "Open release page");
                if (!dialogLifetime.IsCancellationRequested && !_isShuttingDown())
                    statusText.Text = "Unable to open release page.";
            }
        }

        updateButton.Click += (_, _) => _runUiOperation(async () =>
        {
            if (availableVersion is not { } version) return;
            dialog.Hide();
            await Task.Delay(20);
            await RunApplicationUpdateAsync(version);
        }, "Application update");

        void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args) =>
            _runUiOperation(CheckUpdateOnDialogOpenAsync, "Check for application updates");

        async Task CheckUpdateOnDialogOpenAsync()
        {
            try
            {
                var result = await _updateCheckService.CheckAsync(dialogLifetime);
                if (dialogLifetime.IsCancellationRequested || _isShuttingDown()) return;

                switch (result.Status)
                {
                    case UpdateCheckStatus.UpToDate:
                        statusText.Text = result.LatestVersion is { } upToDateVersion
                            ? $"You are up to date.\nLatest version: {upToDateVersion}"
                            : "You are up to date.";
                        break;

                    case UpdateCheckStatus.UpdateAvailable when result.LatestVersion is { } latest:
                        availableVersion = latest;
                        releasePageUri = result.ReleasePageUri;
                        statusText.Text = $"New version {latest} is available.";
                        releaseButton.Visibility = releasePageUri is null
                            ? Visibility.Collapsed
                            : Visibility.Visible;

                        var availability = await _applicationUpdateInstaller.GetAvailabilityAsync(
                            dialogLifetime);
                        if (dialogLifetime.IsCancellationRequested || _isShuttingDown()) return;
                        updateButton.Visibility = availability.IsAvailable
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                        break;

                    default:
                        statusText.Text = "Unable to check for updates.";
                        break;
                }
            }
            catch (OperationCanceledException) when (dialogLifetime.IsCancellationRequested || _isShuttingDown())
            {
            }
            catch (Exception exception)
            {
                _reportUiFailure(exception, "Check for application updates");
                if (!dialogLifetime.IsCancellationRequested && !_isShuttingDown())
                    statusText.Text = "Unable to check for updates.";
            }
        }

        void Dialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args) =>
            cancellation.Cancel();

        dialog.Opened += Dialog_Opened;
        dialog.Closed += Dialog_Closed;
        try { await _showDialog(dialog); }
        finally
        {
            dialog.Opened -= Dialog_Opened;
            dialog.Closed -= Dialog_Closed;
            cancellation.Cancel();
        }
    }

    private async Task RunApplicationUpdateAsync(ReleaseVersion expectedVersion)
    {
        if (!_applicationUpdateActivity.TryBegin())
            return;

        try
        {
            if (_isHistoryRewriteInProgress() || _isRepositoryMaintenanceInProgress())
            {
                await ShowUpdateMessageAsync(
                    "Update unavailable",
                    "Finish the current repository operation before updating CSharpGit.");
                return;
            }

            if (_hasUnappliedCommitMessage() && !await _confirmCloseAsync())
                return;

            using var cancellation = new CancellationTokenSource();
            var statusText = new TextBlock
            {
                Text = "Updating Homebrew metadata...",
                TextWrapping = TextWrapping.Wrap
            };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new ProgressRing
            {
                IsActive = true,
                Width = 24,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            content.Children.Add(statusText);

            var canCancel = true;
            var dialogOpen = true;
            var progressDialog = new ContentDialog
            {
                XamlRoot = _xamlRoot(),
                Title = "Updating CSharpGit",
                Content = content,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            progressDialog.CloseButtonClick += (_, args) =>
            {
                if (!canCancel)
                {
                    args.Cancel = true;
                    return;
                }

                cancellation.Cancel();
            };
            progressDialog.Closed += (_, _) => dialogOpen = false;

            var progress = new Progress<ApplicationUpdateProgress>(value =>
            {
                statusText.Text = value.Message;
                canCancel = value.CanCancel;
                progressDialog.CloseButtonText = value.CanCancel ? "Cancel" : string.Empty;
            });

            var dialogOperation = progressDialog.ShowAsync();
            ApplicationUpdateResult? result = null;
            var wasCancelled = false;
            try
            {
                result = await _applicationUpdateInstaller.InstallAsync(
                    expectedVersion,
                    progress,
                    cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                wasCancelled = true;
            }
            finally
            {
                if (dialogOpen)
                    progressDialog.Hide();

                await dialogOperation;
            }

            if (wasCancelled || result is null)
                return;

            if (result.Status == ApplicationUpdateResultStatus.Succeeded)
            {
                _applicationUpdateActivity.End();
                _completeRestart();
                return;
            }

            await ShowUpdateMessageAsync(
                result.Status == ApplicationUpdateResultStatus.PackageNotPublished
                    ? "Update not available in Homebrew yet"
                    : "Unable to update CSharpGit",
                result.Message);
        }
        finally
        {
            _applicationUpdateActivity.End();
        }
    }

    private async Task ShowUpdateMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await _showDialog(dialog);
    }
}
