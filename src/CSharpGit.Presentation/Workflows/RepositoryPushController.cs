using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

internal sealed class RepositoryPushController(
    OpenRepositoryViewModel viewModel,
    Func<XamlRoot?> xamlRoot,
    Func<ContentDialog, Task<ContentDialogResult>> showDialog,
    Func<string, string, Task> showError,
    Action refreshPresentation,
    Func<bool> isShuttingDown)
{
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly Func<XamlRoot?> _xamlRoot = xamlRoot;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog = showDialog;
    private readonly Func<string, string, Task> _showError = showError;
    private readonly Action _refreshPresentation = refreshPresentation;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;

    internal Task PushPrimaryAsync() => _viewModel.RepositorySync.DefaultPushAction switch
    {
        PushToolbarAction.PushTo => PushToAsync(),
        PushToolbarAction.ForcePushWithLease => ForcePushWithLeaseAsync(),
        _ => PushAsync()
    };

    internal async Task PushAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;
        var repository = _viewModel.Repository;

        var result = await _viewModel.RepositorySync.PushAsync(repository);
        if (!IsCurrent(repository)) return;
        _refreshPresentation();

        switch (result.Kind)
        {
            case PushExecutionKind.Completed:
                return;
            case PushExecutionKind.PublishTargetRequired:
                await OfferChoosePublishTargetAsync(repository,
                    result.Message ?? "Choose the publish target explicitly to continue.");
                return;
            case PushExecutionKind.NonFastForwardRejected:
                await ShowNonFastForwardDialogAsync(repository);
                return;
            case PushExecutionKind.Unavailable:
                await _showError("Push unavailable", result.Message ?? "Push is unavailable.");
                return;
            default:
                await _showError("Push failed", result.Message ?? "Push failed.");
                return;
        }
    }

    private async Task OfferChoosePublishTargetAsync(Repository repository, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot(),
            Title = "Publish target required",
            Content = message,
            PrimaryButtonText = "Choose publish target…",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await _showDialog(dialog) == ContentDialogResult.Primary && IsCurrent(repository))
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

    internal async Task PushToAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;

        if (_viewModel.RepositoryOperations.CurrentOperation != RepositoryOperation.None)
        {
            await _showError("Push to unavailable", "Complete or abort the current Git operation first.");
            return;
        }

        var currentBranch = _viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent);
        if (currentBranch is null)
        {
            await _showError("Push to unavailable", "HEAD is detached. Push to requires a current local branch.");
            return;
        }

        if (_viewModel.RepositorySync.Remotes.Count == 0)
        {
            await _showError("Push to unavailable", "No Git remotes are configured for this repository.");
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
        if (!IsCurrent(repository)) return null;
        if (!preparationResult.Succeeded || preparationResult.Preparation is not { } preparation)
        {
            await _showError(
                $"{options.Title} unavailable",
                preparationResult.ErrorMessage ?? "Could not prepare the publish target.");
            return null;
        }

        var localBranchBox = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            Text = preparation.LocalBranch,
            IsReadOnly = true
        };
        var remoteCombo = new ComboBox
        { Style = UiStyles.Resolve<Style>("CompactComboBoxStyle"),
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
        { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            Text = preparation.LocalBranch,
            PlaceholderText = "Remote branch name"
        };
        var trackCheck = new CheckBox
        { Style = UiStyles.Resolve<Style>("CompactCheckBoxStyle"),
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
            XamlRoot = _xamlRoot(),
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

        if (await _showDialog(dialog) != ContentDialogResult.Primary || !IsCurrent(repository)) return null;
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

        if (!IsCurrent(repository)) return;
        _refreshPresentation();
        if (result.Kind == PushExecutionKind.Completed) return;

        if (result.Kind == PushExecutionKind.NonFastForwardRejected)
        {
            await ShowNonFastForwardDialogAsync(repository);
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
            await _showError(failureTitle, result.Message);
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
            XamlRoot = _xamlRoot(),
            Title = "Push rejected",
            Content = "The remote history differs from your local history. This can happen after rebase or amend.",
            PrimaryButtonText = "Force push with lease…",
            SecondaryButtonText = "Fetch",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        var result = await _showDialog(dialog);
        if (!IsCurrent(repository)) return;
        if (result == ContentDialogResult.Secondary)
        {
            await _viewModel.RepositorySync.FetchAllAsync(repository);
            if (IsCurrent(repository)) _refreshPresentation();
        }
        else if (result == ContentDialogResult.Primary)
        {
            await ForcePushWithLeaseAsync();
        }
    }

    internal async Task ForcePushWithLeaseAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;
        if (_viewModel.RepositoryOperations.CurrentOperation != RepositoryOperation.None)
        {
            await _showError("Force push with lease unavailable", "Complete or abort the current Git operation first.");
            return;
        }

        var repository = _viewModel.Repository;
        var preparation = await _viewModel.RepositorySync.PrepareForcePushWithLeaseAsync(repository);
        if (!IsCurrent(repository)) return;

        if (preparation.Kind == ForcePushPreparationKind.ExplicitTargetRequired)
        {
            var currentBranch = _viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent);
            if (currentBranch is null)
            {
                await _showError(
                    "Force push with lease unavailable",
                    "No current local branch is available.");
                return;
            }

            if (_viewModel.RepositorySync.Remotes.Count == 0)
            {
                await _showError(
                    "Force push with lease unavailable",
                    "No Git remotes are configured for this repository.");
                return;
            }

            var target = await ShowForcePushTargetDialogAsync(currentBranch.Name);
            if (target is null || !IsCurrent(repository)) return;

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

        if (!IsCurrent(repository)) return;

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
            XamlRoot = _xamlRoot(),
            Title = "Force push with lease",
            Content = confirmation,
            PrimaryButtonText = "Force push with lease",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await _showDialog(dialog) != ContentDialogResult.Primary || !IsCurrent(repository)) return;

        var execution = await _viewModel.RepositorySync.ForcePushWithLeaseAsync(repository, snapshot);
        if (!IsCurrent(repository)) return;
        _refreshPresentation();

        switch (execution.Kind)
        {
            case ForcePushExecutionKind.Completed:
                return;
            case ForcePushExecutionKind.Cancelled:
                await _showError(
                    "Force push cancelled",
                    execution.Message ?? "Force push with lease was cancelled.");
                return;
            case ForcePushExecutionKind.LeaseRejected:
                await ShowLeaseRejectedAsync(repository, snapshot);
                return;
            case ForcePushExecutionKind.StaleRepository:
                return;
            default:
                await _showError(
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
            XamlRoot = _xamlRoot(),
            Title = "Force push rejected",
            Content = $"{snapshot.Remote}/{snapshot.RemoteBranch} changed after it was checked.\n\nExpected: {ShortOid(snapshot.ExpectedRemoteCommit)}\n\nThe remote branch contains a different state. Your force push was not performed.",
            PrimaryButtonText = "Fetch",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        if (await _showDialog(rejection) == ContentDialogResult.Primary && IsCurrent(repository))
        {
            await _viewModel.RepositorySync.FetchAsync(repository, snapshot.Remote);
            if (IsCurrent(repository)) _refreshPresentation();
        }
    }

    private async Task<(string Remote, string RemoteBranch)?> ShowForcePushTargetDialogAsync(string localBranch)
    {
        var localBranchBox = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            Text = localBranch,
            IsReadOnly = true
        };
        var remoteCombo = new ComboBox
        { Style = UiStyles.Resolve<Style>("CompactComboBoxStyle"),
            ItemsSource = _viewModel.RepositorySync.Remotes,
            DisplayMemberPath = nameof(GitRemote.Name),
            PlaceholderText = "Select remote",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var remoteBranchBox = new TextBox
        { Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
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
            XamlRoot = _xamlRoot(),
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

        if (await _showDialog(dialog) != ContentDialogResult.Primary) return null;
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
        await _showError(
            title,
            preparation.Message ?? "Force push with lease is unavailable.");
    }

    private bool IsCurrent(Repository repository) =>
        !_isShuttingDown() && ReferenceEquals(repository, _viewModel.Repository);

    private static string ShortOid(string oid) => oid[..Math.Min(10, oid.Length)];
    private static string FormatSubject(string? subject) => string.IsNullOrWhiteSpace(subject) ? string.Empty : $"  {subject}";

}
