using CSharpGit.Application.Abstractions;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private async Task PushFromUiAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;

        var currentBranch = _viewModel.LocalBranches.FirstOrDefault(branch => branch.IsCurrent);
        if (currentBranch is null)
        {
            await ShowErrorAsync(
                "Push unavailable",
                "HEAD is detached. Publishing requires a current local branch.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentBranch.Upstream))
        {
            await RunOrdinaryPushAsync();
            return;
        }

        if (!_settingsWindowController.AutoSetupRemoteOnPush)
        {
            await ShowPublishBranchDialogAsync();
            return;
        }

        await RunOrdinaryPushAsync(
            new PushOptions(AutoSetupRemote: true),
            offerPublishTargetOnDestinationFailure: true);
    }

    private async Task RunOrdinaryPushAsync(
        PushOptions? options = null,
        bool offerPublishTargetOnDestinationFailure = false)
    {
        if (_viewModel.Repository is null) return;
        var repository = _viewModel.Repository;

        try
        {
            await _repositorySyncService.PushAsync(repository, options);
            await RefreshAfterRemoteOperationAsync();
        }
        catch (PushRejectedException exception)
            when (offerPublishTargetOnDestinationFailure
                  && exception.ResultKind == PushResultKind.PushDestinationUnavailable)
        {
            await RefreshAfterRemoteOperationAsync();
            await OfferChoosePublishTargetAsync(
                "Git could not determine a push destination from the current configuration.");
        }
        catch (PushRejectedException exception)
            when (exception.ResultKind == PushResultKind.NonFastForwardRejected)
        {
            await RefreshAfterRemoteOperationAsync();
            if (offerPublishTargetOnDestinationFailure)
            {
                await OfferChoosePublishTargetAsync(
                    "The destination selected by Git has different history. Choose the publish target explicitly to continue safely.");
            }
            else
            {
                await ShowNonFastForwardDialogAsync(repository);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RefreshAfterRemoteOperationAsync();
            await ShowErrorAsync("Push failed", exception.Message);
        }
    }

    private async Task OfferChoosePublishTargetAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Publish target required",
            Content = message,
            PrimaryButtonText = "Choose publish target…",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ShowPublishBranchDialogAsync();
    }

    private async Task ShowPublishBranchDialogAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;
        var repository = _viewModel.Repository;

        PublishBranchPreparation preparation;
        try
        {
            preparation = await _repositorySyncService.PreparePublishBranchAsync(repository);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Publish branch unavailable", exception.Message);
            return;
        }

        var localBranchBox = new TextBox
        {
            Text = preparation.LocalBranch,
            IsReadOnly = true
        };
        var remoteCombo = new ComboBox
        {
            ItemsSource = _viewModel.Remotes,
            DisplayMemberPath = nameof(GitRemote.Name),
            PlaceholderText = "Select remote",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (!string.IsNullOrWhiteSpace(preparation.SuggestedRemote))
        {
            remoteCombo.SelectedItem = _viewModel.Remotes.FirstOrDefault(
                remote => string.Equals(
                    remote.Name,
                    preparation.SuggestedRemote,
                    StringComparison.Ordinal));
        }

        var remoteBranchBox = new TextBox
        {
            Text = preparation.LocalBranch,
            PlaceholderText = "Remote branch name"
        };
        var trackCheck = new CheckBox
        {
            Content = "Track this remote branch as upstream",
            IsChecked = true
        };

        var content = new StackPanel
        {
            Width = 520,
            Spacing = 8
        };
        content.Children.Add(new TextBlock { Text = "Local branch" });
        content.Children.Add(localBranchBox);
        content.Children.Add(new TextBlock { Text = "Remote", Margin = new Thickness(0, 8, 0, 0) });
        content.Children.Add(remoteCombo);
        content.Children.Add(new TextBlock { Text = "Remote branch", Margin = new Thickness(0, 8, 0, 0) });
        content.Children.Add(remoteBranchBox);
        content.Children.Add(trackCheck);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Publish branch",
            Content = content,
            PrimaryButtonText = "Publish",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        void UpdatePrimaryState() =>
            dialog.IsPrimaryButtonEnabled =
                remoteCombo.SelectedItem is GitRemote
                && !string.IsNullOrWhiteSpace(remoteBranchBox.Text);

        remoteCombo.SelectionChanged += (_, _) => UpdatePrimaryState();
        remoteBranchBox.TextChanged += (_, _) => UpdatePrimaryState();
        UpdatePrimaryState();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (remoteCombo.SelectedItem is not GitRemote selectedRemote) return;

        var remoteBranch = remoteBranchBox.Text.Trim();
        _viewModel.SelectedRemote = selectedRemote;
        _viewModel.PushBranchName = remoteBranch;
        _viewModel.SetUpstream = trackCheck.IsChecked == true;

        try
        {
            await _repositorySyncService.PublishBranchAsync(
                repository,
                new PublishBranchRequest(
                    selectedRemote.Name,
                    remoteBranch,
                    trackCheck.IsChecked == true));
            await RefreshAfterRemoteOperationAsync();
        }
        catch (PushRejectedException exception)
            when (exception.ResultKind == PushResultKind.NonFastForwardRejected)
        {
            await RefreshAfterRemoteOperationAsync();
            await ShowNonFastForwardDialogAsync(repository);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RefreshAfterRemoteOperationAsync();
            await ShowErrorAsync("Publish branch failed", exception.Message);
        }
    }

    private async Task RunExplicitPushFromOperationsAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;

        if (string.IsNullOrWhiteSpace(_viewModel.PushBranchName))
        {
            await PushFromUiAsync();
            return;
        }

        if (_viewModel.SelectedRemote is not { } remote)
        {
            await ShowErrorAsync("Push target required", "Select a remote for the explicit push target.");
            return;
        }

        var repository = _viewModel.Repository;
        var remoteBranch = _viewModel.PushBranchName.Trim();
        try
        {
            await _repositorySyncService.PublishBranchAsync(
                repository,
                new PublishBranchRequest(remote.Name, remoteBranch, _viewModel.SetUpstream));
            await RefreshAfterRemoteOperationAsync();
        }
        catch (PushRejectedException exception)
            when (exception.ResultKind == PushResultKind.NonFastForwardRejected)
        {
            await RefreshAfterRemoteOperationAsync();
            await ShowNonFastForwardDialogAsync(repository);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RefreshAfterRemoteOperationAsync();
            await ShowErrorAsync("Push failed", exception.Message);
        }
    }

    private async Task ShowNonFastForwardDialogAsync(Repository repository)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Push rejected",
            Content = "The remote history differs from your local history. This can happen after rebase or amend.",
            PrimaryButtonText = "Force push with lease…",
            SecondaryButtonText = "Fetch",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            await _repositorySyncService.FetchAllAsync(repository);
            await RefreshAfterRemoteOperationAsync();
        }
        else if (result == ContentDialogResult.Primary)
        {
            await RunForcePushWithLeaseAsync();
        }
    }

    private async void ExplicitPush_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button)
        {
            GitOperationsDialog.Hide();
            await Task.Delay(20);
        }
        await RunExplicitPushFromOperationsAsync();
    }

    private async Task RunForcePushWithLeaseAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;
        if (_viewModel.CurrentOperation != RepositoryOperation.None)
        {
            await ShowErrorAsync("Force push with lease unavailable", "Complete or abort the current Git operation first.");
            return;
        }

        var repository = _viewModel.Repository;
        ForcePushWithLeaseSnapshot snapshot;
        try
        {
            snapshot = await _repositorySyncService.PrepareForcePushWithLeaseAsync(repository);
        }
        catch (ForcePushWithLeasePreparationException exception)
            when (exception.Failure == ForcePushPreparationFailure.MissingUpstream)
        {
            var explicitRemote = _viewModel.SelectedRemote?.Name;
            var explicitBranch = _viewModel.PushBranchName.Trim();
            if (string.IsNullOrWhiteSpace(explicitRemote) || string.IsNullOrWhiteSpace(explicitBranch))
            {
                await ShowErrorAsync(
                    "Explicit push target required",
                    "This branch has no configured upstream. Select Remote and Remote branch name in Git operations, then choose Force push with lease again.");
                await GitOperationsDialog.ShowAsync();
                return;
            }
            try
            {
                snapshot = await _repositorySyncService.PrepareForcePushWithLeaseAsync(repository, explicitRemote, explicitBranch);
            }
            catch (Exception explicitException) when (explicitException is not OperationCanceledException)
            {
                await ShowForcePreparationFailureAsync(explicitException);
                return;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowForcePreparationFailureAsync(exception);
            return;
        }

        var confirmation = new StackPanel { Spacing = 8, Width = 540 };
        confirmation.Children.Add(new TextBlock { Text = "Rewrite remote branch history?", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        confirmation.Children.Add(new TextBlock { Text = $"Local branch: {snapshot.LocalBranch}" });
        confirmation.Children.Add(new TextBlock { Text = $"Remote branch: {snapshot.Remote}/{snapshot.RemoteBranch}" });
        confirmation.Children.Add(new TextBlock { Text = $"Local: {ShortOid(snapshot.LocalCommit)}{FormatSubject(snapshot.LocalCommitSubject)}" });
        confirmation.Children.Add(new TextBlock { Text = $"Remote: {ShortOid(snapshot.ExpectedRemoteCommit)}{FormatSubject(snapshot.RemoteCommitSubject)}" });
        confirmation.Children.Add(new TextBlock
        {
            Text = "Remote history may be replaced by your local history.",
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Force push with lease",
            Content = confirmation,
            PrimaryButtonText = "Force push with lease",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            // Snapshot is intentionally the exact immutable object shown above.
            await _repositorySyncService.ForcePushWithLeaseAsync(repository, snapshot);
            await RefreshAfterRemoteOperationAsync();
        }
        catch (ForcePushWithLeaseCancelledException exception)
        {
            await RefreshAfterRemoteOperationAsync();
            await ShowErrorAsync("Force push cancelled", exception.Message);
        }
        catch (PushRejectedException exception) when (exception.ResultKind == PushResultKind.LeaseRejected)
        {
            await RefreshAfterRemoteOperationAsync();
            var rejection = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Force push rejected",
                Content = $"{snapshot.Remote}/{snapshot.RemoteBranch} changed after it was checked.\n\nExpected: {ShortOid(snapshot.ExpectedRemoteCommit)}\n\nThe remote branch contains a different state. Your force push was not performed.",
                PrimaryButtonText = "Fetch",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close
            };
            if (await rejection.ShowAsync() == ContentDialogResult.Primary)
            {
                await _repositorySyncService.FetchAsync(repository, snapshot.Remote);
                await RefreshAfterRemoteOperationAsync();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RefreshAfterRemoteOperationAsync();
            await ShowErrorAsync("Force push failed", exception.Message);
        }
    }

    private async Task ShowForcePreparationFailureAsync(Exception exception)
    {
        if (exception is ForcePushWithLeasePreparationException preparation)
        {
            var title = preparation.Failure switch
            {
                ForcePushPreparationFailure.RemoteBranchDoesNotExist => "Remote branch does not exist",
                ForcePushPreparationFailure.MultiplePushDestinations => "Force push with lease unavailable",
                _ => "Force push with lease unavailable"
            };
            await ShowErrorAsync(title, preparation.Message);
            return;
        }
        await ShowErrorAsync("Force push with lease unavailable", exception.Message);
    }

    private async Task RefreshAfterRemoteOperationAsync()
    {
        await _viewModel.RefreshAsyncForDesktopCheck();
        RefreshPresentationCollections();
    }

    private static string ShortOid(string oid) => oid[..Math.Min(10, oid.Length)];
    private static string FormatSubject(string? subject) => string.IsNullOrWhiteSpace(subject) ? string.Empty : $"  {subject}";

    private async void ForcePushWithLease_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button)
        {
            GitOperationsDialog.Hide();
            await Task.Delay(20);
        }
        await RunForcePushWithLeaseAsync();
    }
}
