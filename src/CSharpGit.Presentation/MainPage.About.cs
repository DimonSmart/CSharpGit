using CSharpGit.Application;
using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly IApplicationVersionProvider _applicationVersionProvider = null!;
    private readonly IUpdateCheckService _updateCheckService = null!;
    private readonly IApplicationUpdateInstaller _applicationUpdateInstaller = null!;
    private readonly ISystemUriLauncher _systemUriLauncher = null!;
    private int _applicationUpdateInProgress;

    internal bool IsApplicationUpdateInProgress =>
        Volatile.Read(ref _applicationUpdateInProgress) != 0;

    private async void About_Click(object sender, RoutedEventArgs e) =>
        await ShowAboutAsync();

    private async Task ShowAboutAsync()
    {
        using var cancellation = new CancellationTokenSource();

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
            XamlRoot = XamlRoot,
            Title = "About CSharpGit",
            Content = content,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        Uri? releasePageUri = null;
        ReleaseVersion? availableVersion = null;

        releaseButton.Click += async (_, _) =>
        {
            if (releasePageUri is null) return;

            try
            {
                await _systemUriLauncher.OpenUriAsync(releasePageUri);
            }
            catch (Exception)
            {
                statusText.Text = "Unable to open release page.";
            }
        };

        updateButton.Click += async (_, _) =>
        {
            if (availableVersion is not { } version) return;

            dialog.Hide();
            await Task.Delay(20);
            await RunApplicationUpdateAsync(version);
        };

        async void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            try
            {
                var result = await _updateCheckService.CheckAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();

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
                            cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        updateButton.Visibility = availability.IsAvailable
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                        break;

                    default:
                        statusText.Text = "Unable to check for updates.";
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                statusText.Text = "Unable to check for updates.";
            }
        }

        dialog.Opened += Dialog_Opened;
        dialog.Closed += (_, _) => cancellation.Cancel();
        await dialog.ShowAsync();
    }

    private async Task RunApplicationUpdateAsync(ReleaseVersion expectedVersion)
    {
        if (Interlocked.CompareExchange(ref _applicationUpdateInProgress, 1, 0) != 0)
            return;

        try
        {
            if (IsHistoryRewriteInProgress || IsRepositoryMaintenanceInProgress)
            {
                await ShowUpdateMessageAsync(
                    "Update unavailable",
                    "Finish the current repository operation before updating CSharpGit.");
                return;
            }

            if (RequiresCloseConfirmation && !await ConfirmCloseAsync())
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
                XamlRoot = XamlRoot,
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
                Interlocked.Exchange(ref _applicationUpdateInProgress, 0);
                if (Microsoft.UI.Xaml.Application.Current is App app)
                    app.CompleteApplicationUpdateRestart(this);
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
            Interlocked.Exchange(ref _applicationUpdateInProgress, 0);
        }
    }

    private async Task ShowUpdateMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }
}
