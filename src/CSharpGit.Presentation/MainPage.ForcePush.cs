using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private async Task PushFromUiAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;
        var repository = _viewModel.Repository;

        var result = await _viewModel.RepositorySync.PushAsync(repository);
        RefreshPresentationCollections();

        switch (result.Kind)
        {
            case PushExecutionKind.Completed:
                return;
            case PushExecutionKind.PublishTargetRequired:
                await OfferChoosePublishTargetAsync(
                    result.Message ?? "Choose the publish target explicitly to continue.");
                return;
            case PushExecutionKind.NonFastForwardRejected:
                await ShowNonFastForwardDialogAsync(repository);
                return;
            case PushExecutionKind.Unavailable:
                await ShowErrorAsync("Push unavailable", result.Message ?? "Push is unavailable.");
                return;
            default:
                await ShowErrorAsync("Push failed", result.Message ?? "Push failed.");
                return;
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

        var target = await ShowPushTargetDialogAsync(
            repository,
            new PushTargetDialogOptions(
                "Publish branch",
                "Publish",
                DefaultSetUpstream: true));
        if (target is null) return;

        await RunTargetPushAsync(repository, target, "Publish branch failed");
    }

    private async Task ShowPushToDialogAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;

        if (_viewModel.RepositoryOperations.CurrentOperation != RepositoryOperation.None)
        {
            await ShowErrorAsync("Push to unavailable", "Complete or abort the current Git operation first.");
            return;
        }

        var currentBranch = _viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent);
        if (currentBranch is null)
        {
            await ShowErrorAsync("Push to unavailable", "HEAD is detached. Push to requires a current local branch.");
            return;
        }

        if (_viewModel.RepositorySync.Remotes.Count == 0)
        {
            await ShowErrorAsync("Push to unavailable", "No Git remotes are configured for this repository.");
            return;
        }

        var repository = _viewModel.Repository;
        var target = await ShowPushTargetDialogAsync(
            repository,
            new PushTargetDialogOptions(
                "Push to",
                "Push",
                DefaultSetUpstream: string.IsNullOrWhiteSpace(currentBranch.Upstream)));
        if (target is null) return;

        await RunTargetPushAsync(repository, target, "Push failed");
    }

    private async Task<PushTargetDialogResult?> ShowPushTargetDialogAsync(
        Repository repository,
        PushTargetDialogOptions options)
    {
        var preparationResult = await _viewModel.RepositorySync.PreparePublishBranchAsync(repository);
        if (!preparationResult.Succeeded || preparationResult.Preparation is not { } preparation)
        {
            await ShowErrorAsync(
                $"{options.Title} unavailable",
                preparationResult.ErrorMessage ?? "Could not prepare the publish target.");
            return null;
        }

        var localBranchBox = new TextBox
        {
            Text = preparation.LocalBranch,
            IsReadOnly = true
        };
        var remoteCombo = new ComboBox
        {
            ItemsSource = _viewModel.RepositorySync.Remotes,
            DisplayMemberPath = nameof(GitRemote.Name),
            PlaceholderText = "Select remote",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (!string.IsNullOrWhiteSpace(preparation.SuggestedRemote))
        {
            remoteCombo.SelectedItem = _viewModel.RepositorySync.Remotes.FirstOrDefault(
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
            IsChecked = options.DefaultSetUpstream
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
            Title = options.Title,
            Content = content,
            PrimaryButtonText = options.PrimaryAction,
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

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        if (remoteCombo.SelectedItem is not GitRemote selectedRemote) return null;

        return new PushTargetDialogResult(
            selectedRemote.Name,
            remoteBranchBox.Text.Trim(),
            trackCheck.IsChecked == true);
    }

    private async Task RunTargetPushAsync(
        Repository repository,
        PushTargetDialogResult target,
        string failureTitle)
    {
        if (!ReferenceEquals(repository, _viewModel.Repository)
            || _viewModel.IsBusy
            || _viewModel.RepositoryOperations.CurrentOperation != RepositoryOperation.None)
            return;

        var result = await _viewModel.RepositorySync.PublishBranchAsync(
            repository,
            target.Remote,
            target.RemoteBranch,
            target.SetUpstream);

        RefreshPresentationCollections();
        if (result.Kind == PushExecutionKind.Completed) return;

        if (result.Kind == PushExecutionKind.NonFastForwardRejected)
        {
            await ShowNonFastForwardDialogAsync(repository);
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
            await ShowErrorAsync(failureTitle, result.Message);
    }

    private sealed record PushTargetDialogOptions(
        string Title,
        string PrimaryAction,
        bool DefaultSetUpstream);

    private sealed record PushTargetDialogResult(
        string Remote,
        string RemoteBranch,
        bool SetUpstream);

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
            await _viewModel.RepositorySync.FetchAllAsync(repository);
            RefreshPresentationCollections();
        }
        else if (result == ContentDialogResult.Primary)
        {
            await RunForcePushWithLeaseAsync();
        }
    }

    private async Task RunForcePushWithLeaseAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;
        if (_viewModel.RepositoryOperations.CurrentOperation != RepositoryOperation.None)
        {
            await ShowErrorAsync("Force push with lease unavailable", "Complete or abort the current Git operation first.");
            return;
        }

        var repository = _viewModel.Repository;
        var preparation = await _viewModel.RepositorySync.PrepareForcePushWithLeaseAsync(repository);

        if (preparation.Kind == ForcePushPreparationKind.ExplicitTargetRequired)
        {
            var currentBranch = _viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent);
            if (currentBranch is null)
            {
                await ShowErrorAsync(
                    "Force push with lease unavailable",
                    "No current local branch is available.");
                return;
            }

            if (_viewModel.RepositorySync.Remotes.Count == 0)
            {
                await ShowErrorAsync(
                    "Force push with lease unavailable",
                    "No Git remotes are configured for this repository.");
                return;
            }

            var target = await ShowForcePushTargetDialogAsync(currentBranch.Name);
            if (target is null) return;

            preparation = await _viewModel.RepositorySync.PrepareForcePushWithLeaseAsync(
                repository,
                target.Value.Remote,
                target.Value.RemoteBranch);
        }

        if (preparation.Kind != ForcePushPreparationKind.Ready
            || preparation.Snapshot is not { } snapshot)
        {
            await ShowForcePreparationFailureAsync(preparation);
            return;
        }

        if (!ReferenceEquals(repository, _viewModel.Repository))
            return;

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

        var execution = await _viewModel.RepositorySync.ForcePushWithLeaseAsync(repository, snapshot);
        RefreshPresentationCollections();

        switch (execution.Kind)
        {
            case ForcePushExecutionKind.Completed:
                return;
            case ForcePushExecutionKind.Cancelled:
                await ShowErrorAsync(
                    "Force push cancelled",
                    execution.Message ?? "Force push with lease was cancelled.");
                return;
            case ForcePushExecutionKind.LeaseRejected:
                await ShowLeaseRejectedAsync(repository, snapshot);
                return;
            case ForcePushExecutionKind.StaleRepository:
                return;
            default:
                await ShowErrorAsync(
                    "Force push failed",
                    execution.Message ?? "Force push with lease failed.");
                return;
        }
    }

    private async Task ShowLeaseRejectedAsync(
        Repository repository,
        ForcePushWithLeaseSnapshot snapshot)
    {
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
            await _viewModel.RepositorySync.FetchAsync(repository, snapshot.Remote);
            RefreshPresentationCollections();
        }
    }

    private async Task<(string Remote, string RemoteBranch)?> ShowForcePushTargetDialogAsync(string localBranch)
    {
        var localBranchBox = new TextBox
        {
            Text = localBranch,
            IsReadOnly = true
        };
        var remoteCombo = new ComboBox
        {
            ItemsSource = _viewModel.RepositorySync.Remotes,
            DisplayMemberPath = nameof(GitRemote.Name),
            PlaceholderText = "Select remote",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var remoteBranchBox = new TextBox
        {
            Text = localBranch,
            PlaceholderText = "Remote branch name"
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

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Force push target",
            Content = content,
            PrimaryButtonText = "Continue",
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

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        if (remoteCombo.SelectedItem is not GitRemote selectedRemote) return null;

        return (selectedRemote.Name, remoteBranchBox.Text.Trim());
    }

    private async Task ShowForcePreparationFailureAsync(ForcePushPreparationResult preparation)
    {
        if (preparation.Kind == ForcePushPreparationKind.StaleRepository)
            return;

        var title = preparation.Kind switch
        {
            ForcePushPreparationKind.RemoteBranchDoesNotExist => "Remote branch does not exist",
            ForcePushPreparationKind.MultiplePushDestinations => "Force push with lease unavailable",
            _ => "Force push with lease unavailable"
        };
        await ShowErrorAsync(
            title,
            preparation.Message ?? "Force push with lease is unavailable.");
    }

    private static string ShortOid(string oid) => oid[..Math.Min(10, oid.Length)];
    private static string FormatSubject(string? subject) => string.IsNullOrWhiteSpace(subject) ? string.Empty : $"  {subject}";

    private async void PushTo_Click(object sender, RoutedEventArgs e)
    {
        await ShowPushToDialogAsync();
    }

    private async void ForcePushWithLease_Click(object sender, RoutedEventArgs e)
    {
        await RunForcePushWithLeaseAsync();
    }
}
