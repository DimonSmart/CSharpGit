using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Views;

public sealed partial class RepositoryToolbarView : UserControl
{
    private Action<RepositoryToolbarAction>? _onAction;

    public RepositoryToolbarView() => InitializeComponent();

    internal void Configure(Action<RepositoryToolbarAction> onAction) =>
        _onAction = onAction ?? throw new ArgumentNullException(nameof(onAction));

    internal void Shutdown() => _onAction = null;

    internal string CommitNavigationLabel => CommitNavigationText.Text;
    internal bool IsRefreshBusy => RefreshProgressRing.IsActive;

    internal void SetCommitNavigationText(bool workingTreeVisible, int changes) =>
        CommitNavigationText.Text = workingTreeVisible ? "Back to history" : $"Commit ({changes})";

    internal void SetOptimizeAvailability(bool isEnabled) =>
        OptimizeRepositoryMenuItem.IsEnabled = isEnabled;

    internal void SetRefreshPresentation(bool refreshing, bool required, string tooltip)
    {
        RefreshIcon.Visibility = !refreshing && !required ? Visibility.Visible : Visibility.Collapsed;
        RefreshRequiredIcon.Visibility = !refreshing && required ? Visibility.Visible : Visibility.Collapsed;
        RefreshProgressRing.Visibility = refreshing ? Visibility.Visible : Visibility.Collapsed;
        RefreshProgressRing.IsActive = refreshing;
        RefreshProgressRing.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Goldenrod);
        ToolTipService.SetToolTip(RefreshButton, tooltip);
    }

    private void CommitNavigation_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.CommitNavigation);

    private void Fetch_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.Fetch);

    private void Pull_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.Pull);

    private void PullFastForwardOnly_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.PullFastForwardOnly);

    private void PullMerge_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.PullMerge);

    private void PullRebase_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.PullRebase);

    private void PullSettings_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.PullSettings);

    private void PushPrimary_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.PushPrimary);

    private void Push_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.Push);

    private void PushTo_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.PushTo);

    private void ForcePushWithLease_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.ForcePushWithLease);

    private void PushSetDefault_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.PushSetDefault);

    private void PushToSetDefault_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.PushToSetDefault);

    private void ForcePushWithLeaseSetDefault_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.ForcePushWithLeaseSetDefault);

    private void RefreshIndicator_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.RefreshIndicator);

    private void OpenRepository_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.OpenRepository);

    private void CloneRepository_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.CloneRepository);

    private void CreateRepository_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.CreateRepository);

    private void Repositories_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.Repositories);

    private void CloseRepository_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.CloseRepository);

    private void RepositorySettings_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.RepositorySettings);

    private void OptimizeRepository_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.OptimizeRepository);

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.Settings);

    private void About_Click(object sender, RoutedEventArgs e) =>
        _onAction?.Invoke(RepositoryToolbarAction.About);
}
