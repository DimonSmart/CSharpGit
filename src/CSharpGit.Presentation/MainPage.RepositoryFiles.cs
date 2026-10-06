using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly RepositoryFilesViewModel _repositoryFilesViewModel = null!;
    private PivotItem? _repositoryFilesTab;
    private PivotItem? _changesTab;
    private TextBox? _repositoryFilesSearch;
    private ComboBox? _repositoryFilesSearchMode;
    private TreeView? _repositoryFilesTree;
    private ListView? _repositoryContentResults;
    private ProgressRing? _repositoryFilesProgress;
    private TextBlock? _repositoryFilesStatus;
    private bool _repositoryFilesBuildingTree;
    private bool _repositoryFilesRestoringPresentationState;

    private MainPage(
        OpenRepositoryViewModel viewModel,
        IHistoryService historyService,
        IReferenceService referenceService,
        IRepositorySyncService repositorySyncService,
        ICommitActionService commitActionService,
        TagsViewModel tagsViewModel,
        IWorkingTreeStatusReader workingTreeStatusReader,
        IRepositoryFileVersionService fileVersionService,
        IDesktopShellService desktopShellService,
        IRepositoryPathService repositoryPathService,
        IExternalGitToolService externalGitToolService,
        RepositoryFilesViewModel repositoryFilesViewModel)
        : this(
            viewModel,
            historyService,
            referenceService,
            repositorySyncService,
            commitActionService,
            tagsViewModel,
            workingTreeStatusReader,
            fileVersionService,
            desktopShellService,
            repositoryPathService,
            externalGitToolService)
    {
        _repositoryFilesViewModel = repositoryFilesViewModel
            ?? throw new ArgumentNullException(nameof(repositoryFilesViewModel));
        _repositoryFilesViewModel.Attach(_viewModel);
        InitializeRepositoryFiles();
    }

    private void InitializeRepositoryFiles()
    {
        _changesTab = ChangesTab;
        _changesTab.Name = "ChangesTab";
        _repositoryFilesTab = new PivotItem { Header = "Files", Name = "FilesTab" };
        _repositoryFilesTab.Content = BuildRepositoryFilesSurface();
        DetailsTabs.Items.Add(_repositoryFilesTab);

        CommitFilesList.DoubleTapped -= CommitFilesList_DoubleTapped;
        CommitFilesList.KeyDown -= CommitFilesList_KeyDown;
        CommitFilesList.DoubleTapped += RepositoryChangedFiles_DoubleTapped;
        CommitFilesList.KeyDown += RepositoryChangedFiles_KeyDown;

        _repositoryFilesViewModel.PropertyChanged += RepositoryFilesViewModel_PropertyChanged;
        SynchronizeRepositoryFilesSearchControls();
        UpdateRepositoryFilesModeSurface();
        SetRepositoryFilesStatus(
            _repositoryFilesViewModel.StatusText,
            _repositoryFilesViewModel.IsLoading);
    }

    private UIElement BuildRepositoryFilesSurface()
    {
        var root = new Grid { MinHeight = 120 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Grid
        {
            Padding = new Thickness(8, 4, 8, 4),
            ColumnSpacing = 8
        };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _repositoryFilesSearch = new TextBox
        {
            PlaceholderText = "Search files...",
            MinWidth = 180
        };
        _repositoryFilesSearch.TextChanged += RepositoryFilesSearch_TextChanged;
        _repositoryFilesSearch.KeyDown += RepositoryFilesSearch_KeyDown;
        toolbar.Children.Add(_repositoryFilesSearch);

        _repositoryFilesSearchMode = new ComboBox
        {
            MinWidth = 110,
            ItemsSource = new[]
            {
                RepositoryFilesViewModel.NameSearchMode,
                RepositoryFilesViewModel.ContentSearchMode
            },
            SelectedIndex = 0
        };
        _repositoryFilesSearchMode.SelectionChanged += RepositoryFilesSearchMode_SelectionChanged;
        Grid.SetColumn(_repositoryFilesSearchMode, 1);
        toolbar.Children.Add(_repositoryFilesSearchMode);
        root.Children.Add(toolbar);

        var leftPane = new Grid();

        _repositoryFilesTree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["DenseTreeViewStyle"],
            ItemContainerStyle = (Style)Microsoft.UI.Xaml.Application.Current.Resources["DenseTreeItemStyle"],
            ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources["RepositoryFilesTreeItemTemplate"],
            ItemsSource = _repositoryFilesViewModel.TreeRoots
        };
        _repositoryFilesTree.SelectionChanged += RepositoryFilesTree_SelectionChanged;
        _repositoryFilesTree.DoubleTapped += RepositoryFilesTree_DoubleTapped;
        _repositoryFilesTree.RightTapped += RepositoryFilesTree_RightTapped;
        _repositoryFilesTree.Expanding += RepositoryFilesTree_Expanding;
        _repositoryFilesTree.Collapsed += RepositoryFilesTree_Collapsed;
        leftPane.Children.Add(_repositoryFilesTree);

        _repositoryContentResults = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            Visibility = Visibility.Collapsed,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _repositoryContentResults.SelectionChanged += RepositoryContentResults_SelectionChanged;
        _repositoryContentResults.DoubleTapped += RepositoryContentResults_DoubleTapped;
        _repositoryContentResults.KeyDown += RepositoryContentResults_KeyDown;
        _repositoryContentResults.RightTapped += RepositoryContentResults_RightTapped;
        leftPane.Children.Add(_repositoryContentResults);

        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        _repositoryFilesProgress = new ProgressRing
        {
            Width = 18,
            Height = 18,
            IsActive = false,
            Visibility = Visibility.Collapsed
        };
        _repositoryFilesStatus = new TextBlock
        {
            Text = string.Empty,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
            TextAlignment = TextAlignment.Center
        };
        statusPanel.Children.Add(_repositoryFilesProgress);
        statusPanel.Children.Add(_repositoryFilesStatus);
        leftPane.Children.Add(statusPanel);

        var body = BuildRepositoryFilesSplitBody(leftPane);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return root;
    }

    private void RepositoryFilesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(RepositoryFilesViewModel.StatusText):
            case nameof(RepositoryFilesViewModel.IsLoading):
                SetRepositoryFilesStatus(
                    _repositoryFilesViewModel.StatusText,
                    _repositoryFilesViewModel.IsLoading);
                break;

            case nameof(RepositoryFilesViewModel.NameQuery):
            case nameof(RepositoryFilesViewModel.SearchMode):
                SynchronizeRepositoryFilesSearchControls();
                UpdateRepositoryFilesModeSurface();
                break;

            case nameof(RepositoryFilesViewModel.ContentResults):
            case nameof(RepositoryFilesViewModel.IsContentSearchMode):
                UpdateRepositoryFilesModeSurface();
                break;

            case nameof(RepositoryFilesViewModel.TreeRevision):
                SynchronizeRepositoryFilesTreeSelection();
                break;

            case nameof(RepositoryFilesViewModel.SelectionRevision):
                SynchronizeRepositoryFilesTreeSelection();
                ApplyRepositoryFileSelectionFromViewModel();
                break;
        }
    }

    private bool IsRepositoryFilesActive =>
        _repositoryFilesTab is not null && ReferenceEquals(DetailsTabs.SelectedItem, _repositoryFilesTab);

    private void UpdateRepositoryFilesViewActivity()
    {
        if (!IsRepositoryFilesActive)
            CancelRepositoryFilePreview();
        _ = _repositoryFilesViewModel.SetActiveAsync(IsRepositoryFilesActive);
    }

    private void SynchronizeRepositoryFilesSearchControls()
    {
        _repositoryFilesRestoringPresentationState = true;
        try
        {
            if (_repositoryFilesSearch is not null
                && !string.Equals(
                    _repositoryFilesSearch.Text,
                    _repositoryFilesViewModel.NameQuery,
                    StringComparison.Ordinal))
            {
                _repositoryFilesSearch.Text = _repositoryFilesViewModel.NameQuery;
            }

            if (_repositoryFilesSearchMode is not null
                && !string.Equals(
                    _repositoryFilesSearchMode.SelectedItem as string,
                    _repositoryFilesViewModel.SearchMode,
                    StringComparison.Ordinal))
            {
                _repositoryFilesSearchMode.SelectedItem = _repositoryFilesViewModel.SearchMode;
            }
        }
        finally
        {
            _repositoryFilesRestoringPresentationState = false;
        }
    }

    private void SynchronizeRepositoryFilesTreeSelection()
    {
        if (_repositoryFilesTree is null) return;
        _repositoryFilesBuildingTree = true;
        try
        {
            _repositoryFilesTree.SelectedItem = _repositoryFilesViewModel.SelectedTreeNode;
        }
        finally
        {
            _repositoryFilesBuildingTree = false;
        }
    }

    private void ApplyRepositoryFileSelectionFromViewModel()
    {
        if (_repositoryFilesViewModel.SelectedPath is not { } path)
        {
            ClearRepositoryFileSelection();
            return;
        }

        ApplyRepositoryFileSelection(
            path,
            _repositoryFilesViewModel.SelectedLineNumber,
            _repositoryFilesViewModel.SelectedEntry);
    }

    private void RepositoryFilesSearch_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (_repositoryFilesRestoringPresentationState || _repositoryFilesSearch is null)
            return;
        _repositoryFilesViewModel.SetNameQuery(_repositoryFilesSearch.Text);
    }

    private async void RepositoryFilesSearch_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter
            || !_repositoryFilesViewModel.IsContentSearchMode
            || _repositoryFilesSearch is null)
        {
            return;
        }

        args.Handled = true;
        await _repositoryFilesViewModel.SearchContentAsync(_repositoryFilesSearch.Text);
    }

    private void RepositoryFilesSearchMode_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_repositoryFilesRestoringPresentationState
            || _repositoryFilesSearchMode?.SelectedItem is not string mode)
        {
            return;
        }

        _repositoryFilesViewModel.SetSearchMode(
            mode,
            _repositoryFilesSearch?.Text ?? string.Empty);
    }

    private void UpdateRepositoryFilesModeSurface()
    {
        if (_repositoryFilesTree is null || _repositoryContentResults is null) return;

        var content = _repositoryFilesViewModel.IsContentSearchMode;
        _repositoryFilesTree.Visibility = content ? Visibility.Collapsed : Visibility.Visible;
        _repositoryContentResults.Visibility = content ? Visibility.Visible : Visibility.Collapsed;
        _repositoryContentResults.ItemsSource = content
            ? _repositoryFilesViewModel.ContentResults
            : null;
    }

    private void RepositoryFilesTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_repositoryFilesBuildingTree || _repositoryFilesViewModel.IsRebuildingTree)
            return;
        _repositoryFilesViewModel.SelectTreeNode(sender.SelectedItem as RepositorySnapshotTreeNode);
    }

    private async void RepositoryFilesTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (SelectedRepositorySnapshotEntry() is not { Kind: RepositorySnapshotEntryKind.File } entry)
            return;

        await OpenRepositorySnapshotFileAsync(entry, openInEditor: false);
        args.Handled = true;
    }

    private void RepositoryFilesTree_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is not FrameworkElement source
            || SelectedRepositorySnapshotEntry() is not { } entry)
        {
            return;
        }

        ShowRepositorySnapshotFileMenu(source, entry);
        args.Handled = true;
    }

    private void RepositoryFilesTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is RepositorySnapshotTreeNode model)
            _repositoryFilesViewModel.SetExpanded(model, expanded: true);
    }

    private void RepositoryFilesTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Item is RepositorySnapshotTreeNode model)
            _repositoryFilesViewModel.SetExpanded(model, expanded: false);
    }

    private RepositorySnapshotEntry? SelectedRepositorySnapshotEntry() =>
        _repositoryFilesViewModel.SnapshotMatchesSelection
            ? _repositoryFilesViewModel.SelectedEntry
            : null;

    private void RepositoryContentResults_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        _repositoryFilesViewModel.SelectContentResult(
            _repositoryContentResults?.SelectedItem as RepositoryContentSearchRow);
    }

    private async void RepositoryContentResults_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (_repositoryContentResults?.SelectedItem is not RepositoryContentSearchRow row)
            return;

        await OpenRepositoryContentMatchAsync(row, openInEditor: false);
        args.Handled = true;
    }

    private async void RepositoryContentResults_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter
            || _repositoryContentResults?.SelectedItem is not RepositoryContentSearchRow row)
        {
            return;
        }

        args.Handled = true;
        await OpenRepositoryContentMatchAsync(row, openInEditor: false);
    }

    private void RepositoryContentResults_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is not FrameworkElement source
            || _repositoryContentResults?.SelectedItem is not RepositoryContentSearchRow row)
        {
            return;
        }

        var entry = _repositoryFilesViewModel.FindEntry(row.Match.Path);
        if (entry is null) return;

        ShowRepositorySnapshotFileMenu(source, entry);
        args.Handled = true;
    }

    private async Task OpenRepositoryContentMatchAsync(
        RepositoryContentSearchRow row,
        bool openInEditor)
    {
        var entry = _repositoryFilesViewModel.SelectContentResult(row);
        if (entry is not { Kind: RepositorySnapshotEntryKind.File }) return;
        await OpenRepositorySnapshotFileAsync(entry, openInEditor);
    }

    private void ShowRepositorySnapshotFileMenu(
        FrameworkElement target,
        RepositorySnapshotEntry entry)
    {
        var flyout = new MenuFlyout();
        if (entry.Kind == RepositorySnapshotEntryKind.File)
        {
            AddMenuItem(
                flyout,
                "Open",
                true,
                () => OpenRepositorySnapshotFileAsync(entry, openInEditor: false));
            AddMenuItem(
                flyout,
                "Open in editor",
                true,
                () => OpenRepositorySnapshotFileAsync(entry, openInEditor: true));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        AddMenuItem(flyout, "Copy path", true, () => CopyTextAsync(entry.Path));
        flyout.ShowAt(target);
    }

    private async Task OpenRepositorySnapshotFileAsync(
        RepositorySnapshotEntry entry,
        bool openInEditor)
    {
        var repository = _repositoryFilesViewModel.CurrentRepository;
        if (repository is null || !_repositoryFilesViewModel.SnapshotMatchesSelection)
            return;

        try
        {
            var version = await _repositoryFilesViewModel.ResolveFileVersionAsync(entry);
            if (version is null) return;
            if (!version.CanOpen)
            {
                throw new NotSupportedException(
                    version.UnavailableReason ?? "This historical entry cannot be opened.");
            }

            if (!openInEditor)
            {
                await OpenResolvedVersionAsync(repository, version, DiffFileSide.Changed);
                return;
            }

            var materialized = await _fileVersionService.MaterializeAsync(
                repository,
                version,
                DiffFileSide.Changed);
            await _externalGitToolService.OpenEditorAsync(repository, materialized.Path);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException exception)
            when (openInEditor
                  && exception.Message.Contains("editor", StringComparison.OrdinalIgnoreCase))
        {
            await ShowGitEditorUnavailableAsync(exception.Message);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(
                openInEditor
                    ? "Could not open historical file in editor"
                    : "Could not open historical file",
                UserFacingFileError(exception));
        }
    }

    private async Task ShowGitEditorUnavailableAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Git editor is not configured",
            Content = message,
            PrimaryButtonText = "Open Git Tools Settings",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            OpenSettingsWindow(SettingsSection.GitTools);
    }

    private void RepositoryChangedFiles_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (CommitFilesList.SelectedItem is null || _changesTab is null) return;
        DetailsTabs.SelectedItem = _changesTab;
        args.Handled = true;
    }

    private void RepositoryChangedFiles_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || CommitFilesList.SelectedItem is null || _changesTab is null)
            return;

        DetailsTabs.SelectedItem = _changesTab;
        args.Handled = true;
    }

    private void SetRepositoryFilesStatus(string text, bool loading)
    {
        if (_repositoryFilesStatus is not null)
            _repositoryFilesStatus.Text = text;

        if (_repositoryFilesProgress is not null)
        {
            _repositoryFilesProgress.IsActive = loading;
            _repositoryFilesProgress.Visibility = loading
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
