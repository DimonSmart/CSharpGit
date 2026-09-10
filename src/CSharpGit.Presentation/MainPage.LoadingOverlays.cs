using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _loadingOverlayLifecycleHooked;
    private bool _loadingOverlaysInitialized;
    private Grid? _commitLoadingOverlay;
    private Grid? _diffLoadingOverlay;
    private ProgressRing? _commitLoadingRing;
    private ProgressRing? _diffLoadingRing;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_loadingOverlayLifecycleHooked) return;

        _loadingOverlayLifecycleHooked = true;
        Loaded += LoadingOverlays_Loaded;
        Unloaded += LoadingOverlays_Unloaded;
    }

    private void LoadingOverlays_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureLoadingOverlays();
        _viewModel.PropertyChanged -= LoadingOverlayViewModel_PropertyChanged;
        _viewModel.PropertyChanged += LoadingOverlayViewModel_PropertyChanged;
        UpdateLoadingOverlays();
    }

    private void LoadingOverlays_Unloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.PropertyChanged -= LoadingOverlayViewModel_PropertyChanged;
    }

    private void EnsureLoadingOverlays()
    {
        if (_loadingOverlaysInitialized) return;
        if (CompactDiffList.Parent is not Grid diffViewer) return;

        _commitLoadingOverlay = CreateLoadingOverlay("Loading commit…", out _commitLoadingRing);
        Grid.SetRow(_commitLoadingOverlay, 3);
        HistoryPane.Children.Add(_commitLoadingOverlay);

        _diffLoadingOverlay = CreateLoadingOverlay("Loading diff…", out _diffLoadingRing);
        diffViewer.Children.Add(_diffLoadingOverlay);

        _loadingOverlaysInitialized = true;
    }

    private Grid CreateLoadingOverlay(string message, out ProgressRing progressRing)
    {
        progressRing = new ProgressRing
        {
            Width = 24,
            Height = 24
        };

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8
        };
        content.Children.Add(progressRing);
        content.Children.Add(new TextBlock
        {
            Text = message,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var overlay = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = RootLayout.Background
        };
        overlay.Children.Add(content);
        return overlay;
    }

    private void LoadingOverlayViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenRepositoryViewModel.IsCommitLoading)
            or nameof(OpenRepositoryViewModel.IsDiffLoading))
            UpdateLoadingOverlays();
    }

    private void UpdateLoadingOverlays()
    {
        if (!_loadingOverlaysInitialized) return;

        _commitLoadingOverlay!.Visibility = _viewModel.CommitLoadingVisibility;
        _commitLoadingRing!.IsActive = _viewModel.IsCommitLoading;
        _diffLoadingOverlay!.Visibility = _viewModel.DiffLoadingVisibility;
        _diffLoadingRing!.IsActive = _viewModel.IsDiffLoading;
    }
}
