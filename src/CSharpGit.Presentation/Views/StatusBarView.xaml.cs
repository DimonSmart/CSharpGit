using System.ComponentModel;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace CSharpGit.Presentation.Views;

/// <summary>Owns status controls, pulse animation and Git-command summary for one window.</summary>
public sealed partial class StatusBarView : UserControl
{
    private readonly Storyboard _pulse = new();
    private readonly SolidColorBrush _busyBackground = new(Windows.UI.Color.FromArgb(28, 34, 197, 94));
    private bool _busy;
    private bool _disposed;
    private OpenRepositoryViewModel? _repository;

    public StatusBarView()
    {
        InitializeComponent();
        var animation = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1
        });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)), Value = 0.45
        });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800)), Value = 1
        });
        Storyboard.SetTarget(animation, BusyPulseDot);
        Storyboard.SetTargetProperty(animation, "Opacity");
        _pulse.Children.Add(animation);
    }

    public event EventHandler? GitCommandStatusClicked;

    internal void ConfigureRepository(OpenRepositoryViewModel repository)
    {
        if (_repository is not null) throw new InvalidOperationException("Status bar already configured.");
        _repository = repository;
        repository.PropertyChanged += Repository_PropertyChanged;
        RefreshRepositoryStatus();
    }

    private void Repository_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(OpenRepositoryViewModel.Repository)
            or nameof(OpenRepositoryViewModel.HeadDisplay)
            or nameof(OpenRepositoryViewModel.CurrentBranchName)
            or nameof(RepositoryOperationsViewModel.CurrentOperation)
            or nameof(OpenRepositoryViewModel.IsBusy))
            RefreshRepositoryStatus();
    }

    internal void RefreshRepositoryStatus()
    {
        if (_repository is not { } _viewModel || _disposed) return;
        var current = _viewModel.Branches.LocalBranches.FirstOrDefault(branch => branch.IsCurrent);
        var branch = current?.Name
                     ?? _viewModel.CurrentBranchName
                     ?? (_viewModel.Repository is null ? string.Empty : "detached HEAD");
        SetStatus(
            branch,
            current is null ? string.Empty : $"↑{current.Ahead} ↓{current.Behind}",
            _viewModel.WorkingTree.Changes.Count,
            _viewModel.IsBusy ? "Working…" :
                _viewModel.RepositoryOperations.CurrentOperation == RepositoryOperation.None
                    ? "Ready" : _viewModel.RepositoryOperations.CurrentOperation.ToString(),
            _viewModel.IsBusy);
    }

    internal void SetStatus(string branch, string tracking, int changes, string operation, bool busy)
    {
        if (_disposed) return;
        StatusBranchText.Text = branch;
        StatusTrackingText.Text = tracking;
        StatusChangesText.Text = $"{changes} changes";
        StatusOperationText.Text = operation;
        StatusOperationIdleIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        BusyPulseDot.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusOperationContainer.Background = busy ? _busyBackground : null;

        if (_busy == busy) return;
        _busy = busy;
        BusyPulseDot.Opacity = 1;
        if (busy) _pulse.Begin();
        else _pulse.Stop();
    }

    internal void SetGitCommandStatus(string text, string tooltip)
    {
        if (_disposed) return;
        GitCommandStatusText.Text = text;
        ToolTipService.SetToolTip(GitCommandStatusButton, tooltip);
    }

    internal string BranchForCheck => StatusBranchText.Text;

    internal void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        if (_repository is not null)
        {
            _repository.PropertyChanged -= Repository_PropertyChanged;
            _repository = null;
        }
        _pulse.Stop();
        GitCommandStatusClicked = null;
    }

    private void GitCommandStatusButton_Click(object sender, RoutedEventArgs e) =>
        GitCommandStatusClicked?.Invoke(this, EventArgs.Empty);
}
