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
        var repository = _viewModel.Repository;
        var hasExplicitBranch = !string.IsNullOrWhiteSpace(_viewModel.PushBranchName);
        var remote = hasExplicitBranch ? _viewModel.SelectedRemote?.Name : null;
        var branch = hasExplicitBranch ? _viewModel.PushBranchName.Trim() : null;
        if (hasExplicitBranch && string.IsNullOrWhiteSpace(remote))
        {
            await ShowErrorAsync("Push target required", "Select a remote for the explicit push target.");
            return;
        }

        try
        {
            await _referenceService.PushAsync(repository, remote, branch, hasExplicitBranch && _viewModel.SetUpstream);
            await RefreshAfterRemoteOperationAsync();
        }
        catch (PushRejectedException exception) when (exception.ResultKind == PushResultKind.NonFastForwardRejected)
        {
            await RefreshAfterRemoteOperationAsync();
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
                await _referenceService.FetchAllAsync(repository);
                await RefreshAfterRemoteOperationAsync();
            }
            else if (result == ContentDialogResult.Primary)
            {
                await RunForcePushWithLeaseAsync();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RefreshAfterRemoteOperationAsync();
            await ShowErrorAsync("Push failed", exception.Message);
        }
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
            snapshot = await _referenceService.PrepareForcePushWithLeaseAsync(repository);
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
                snapshot = await _referenceService.PrepareForcePushWithLeaseAsync(repository, explicitRemote, explicitBranch);
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
            await _referenceService.ForcePushWithLeaseAsync(repository, snapshot);
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
                await _referenceService.FetchAsync(repository, snapshot.Remote);
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
