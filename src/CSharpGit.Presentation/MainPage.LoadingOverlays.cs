using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _loadingOverlaysInitialized;
    private Grid? _changesLoadingOverlay;
    private Grid? _diffLoadingOverlay;
    private ProgressRing? _changesLoadingRing;
    private ProgressRing? _diffLoadingRing;

    private void InitializeLoadingOverlays()
    {
        if (_loadingOverlaysInitialized || _viewModel is null) return;
        if (ChangedFilesTree.Parent is not Grid filesViewer) return;
        if (CompactDiffList.Parent is not Grid diffViewer) return;

        _changesLoadingOverlay = CreateLoadingOverlay("Loading changes…", out _changesLoadingRing);
        Grid.SetRow(_changesLoadingOverlay, 1);
        filesViewer.Children.Add(_changesLoadingOverlay);

        _diffLoadingOverlay = CreateLoadingOverlay("Loading diff…", out _diffLoadingRing);
        diffViewer.Children.Add(_diffLoadingOverlay);

        _viewModel.PropertyChanged += LoadingOverlayViewModel_PropertyChanged;
        _loadingOverlaysInitialized = true;
        UpdateLoadingOverlays();
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
        if (e.PropertyName is nameof(OpenRepositoryViewModel.IsChangedFilesLoading)
            or nameof(OpenRepositoryViewModel.IsDiffLoading))
            UpdateLoadingOverlays();
    }

    private void UpdateLoadingOverlays()
    {
        if (!_loadingOverlaysInitialized) return;

        _changesLoadingOverlay!.Visibility = _viewModel.ChangedFilesLoadingVisibility;
        _changesLoadingRing!.IsActive = _viewModel.IsChangedFilesLoading;
        _diffLoadingOverlay!.Visibility = _viewModel.DiffLoadingVisibility;
        _diffLoadingRing!.IsActive = _viewModel.IsDiffLoading;
    }

    private void DetachLoadingOverlays()
    {
        if (!_loadingOverlaysInitialized) return;
        _viewModel.PropertyChanged -= LoadingOverlayViewModel_PropertyChanged;
    }
}
