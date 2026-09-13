using System.ComponentModel;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private const int RepositoryContentResultLimit = 500;
    private const int RepositoryFilesStateLimit = 12;

    private IRepositorySnapshotService? _repositorySnapshotService;
    private PivotItem? _repositoryFilesTab;
    private PivotItem? _changesTab;
    private TextBox? _repositoryFilesSearch;
    private ComboBox? _repositoryFilesSearchMode;
    private TreeView? _repositoryFilesTree;
    private ListView? _repositoryContentResults;
    private ProgressRing? _repositoryFilesProgress;
    private TextBlock? _repositoryFilesStatus;
    private readonly RepositorySnapshotCache _repositorySnapshotCache = new();
    private readonly Dictionary<TreeViewNode, RepositorySnapshotTreeNode> _repositoryFilesNodes = [];
    private readonly Dictionary<string, RepositoryFilesPresentationState> _repositoryFilesStates = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _repositoryFilesStateLru = [];
    private readonly Dictionary<string, LinkedListNode<string>> _repositoryFilesStateLruNodes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _repositorySnapshotCts;
    private CancellationTokenSource? _repositoryContentSearchCts;
    private IReadOnlyList<RepositorySnapshotEntry> _repositorySnapshot = [];
    private IReadOnlyList<RepositoryContentSearchMatch> _repositoryContentMatches = [];
    private Repository? _repositorySnapshotRepository;
    private string? _repositorySnapshotCommit;
    private string? _repositoryContentCommit;
    private string? _repositoryFilesLastSelectedPath;
    private long _repositorySnapshotGeneration;
    private long _repositoryContentSearchGeneration;
    private string _repositoryFilesNameQuery = string.Empty;
    private string _repositoryFilesSearchModeName = "Name";
    private bool _repositoryFilesBuildingTree;

    public MainPage(
        OpenRepositoryViewModel viewModel,
        IReferenceHistoryService referenceHistoryService,
        IReferenceService referenceService,
        IWorkingTreeDiffService workingTreeDiffService,
        IRepositoryFileVersionService fileVersionService,
        IDesktopShellService desktopShellService,
        IRepositoryPathService repositoryPathService,
        IGitToolsService gitToolsService,
        IRepositorySnapshotService repositorySnapshotService)
        : this(
            viewModel,
            referenceHistoryService,
            referenceService,
            workingTreeDiffService,
            fileVersionService,
            desktopShellService,
            repositoryPathService,
            gitToolsService)
    {
        _repositorySnapshotService = repositorySnapshotService ?? throw new ArgumentNullException(nameof(repositorySnapshotService));
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

        _viewModel.PropertyChanged += RepositoryFilesViewModel_PropertyChanged;
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
            ItemsSource = new[] { "Name", "Content" },
            SelectedIndex = 0
        };
        _repositoryFilesSearchMode.SelectionChanged += RepositoryFilesSearchMode_SelectionChanged;
        Grid.SetColumn(_repositoryFilesSearchMode, 1);
        toolbar.Children.Add(_repositoryFilesSearchMode);
        root.Children.Add(toolbar);

        var body = new Grid();
        Grid.SetRow(body, 1);

        _repositoryFilesTree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _repositoryFilesTree.ItemInvoked += RepositoryFilesTree_ItemInvoked;
        _repositoryFilesTree.DoubleTapped += RepositoryFilesTree_DoubleTapped;
        _repositoryFilesTree.RightTapped += RepositoryFilesTree_RightTapped;
        _repositoryFilesTree.Expanding += RepositoryFilesTree_Expanding;
        _repositoryFilesTree.Collapsed += RepositoryFilesTree_Collapsed;
        body.Children.Add(_repositoryFilesTree);

        _repositoryContentResults = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            Visibility = Visibility.Collapsed,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _repositoryContentResults.DoubleTapped += RepositoryContentResults_DoubleTapped;
        _repositoryContentResults.KeyDown += RepositoryContentResults_KeyDown;
        _repositoryContentResults.RightTapped += RepositoryContentResults_RightTapped;
        body.Children.Add(_repositoryContentResults);

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
        body.Children.Add(statusPanel);

        root.Children.Add(body);
        return root;
    }

    private void RepositoryFilesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(OpenRepositoryViewModel.Repository))
        {
            CancelRepositoryFilesRequests();
            _repositorySnapshotCache.Clear();
            _repositoryFilesStates.Clear();
            _repositoryFilesStateLru.Clear();
            _repositoryFilesStateLruNodes.Clear();
            _repositorySnapshot = [];
            _repositorySnapshotRepository = null;
            _repositorySnapshotCommit = null;
            _repositoryContentMatches = [];
            _repositoryContentCommit = null;
            _repositoryFilesLastSelectedPath = null;
            ClearRepositoryFilesTree();
            if (IsRepositoryFilesActive) _ = LoadRepositorySnapshotAsync();
        }
        else if (args.PropertyName == nameof(OpenRepositoryViewModel.SelectedHistoryRow) && IsRepositoryFilesActive)
        {
            _ = LoadRepositorySnapshotAsync();
        }
    }

    private bool IsRepositoryFilesActive =>
        _repositoryFilesTab is not null && ReferenceEquals(DetailsTabs.SelectedItem, _repositoryFilesTab);

    private void UpdateRepositoryFilesViewActivity()
    {
        if (IsRepositoryFilesActive)
        {
            _ = LoadRepositorySnapshotAsync();
            return;
        }

        SaveRepositoryFilesPresentationState();
        CancelRepositoryFilesRequests();
    }

    private async Task LoadRepositorySnapshotAsync()
    {
        if (!IsRepositoryFilesActive || _repositorySnapshotService is null) return;
        var repository = _viewModel.Repository;
        var commitHash = _viewModel.SelectedHistoryRow?.Commit.Hash;
        if (repository is null || string.IsNullOrWhiteSpace(commitHash))
        {
            SetRepositoryFilesStatus("Select a commit.", loading: false);
            ClearRepositoryFilesTree();
            return;
        }

        if (ReferenceEquals(repository, _repositorySnapshotRepository)
            && string.Equals(commitHash, _repositorySnapshotCommit, StringComparison.Ordinal))
        {
            PublishRepositorySnapshot(repository, commitHash, _repositorySnapshot);
            return;
        }

        SaveRepositoryFilesPresentationState();
        CancelRepositorySnapshotRequest();
        CancelRepositoryContentSearch();
        var generation = Interlocked.Increment(ref _repositorySnapshotGeneration);
        _repositorySnapshotCts = new CancellationTokenSource();
        var token = _repositorySnapshotCts.Token;
        var repositoryIdentity = RepositoryIdentity(repository);
        _repositoryContentMatches = [];
        _repositoryContentCommit = null;
        UpdateRepositoryFilesModeSurface();

        if (_repositorySnapshotCache.TryGet(repositoryIdentity, commitHash, out var cached))
        {
            if (CanPublishRepositoryFiles(repository, commitHash, generation))
                PublishRepositorySnapshot(repository, commitHash, cached);
            return;
        }

        ClearRepositoryFilesTree();
        SetRepositoryFilesStatus("Loading repository files...", loading: true);
        try
        {
            var snapshot = await _repositorySnapshotService.ReadTreeAsync(repository, commitHash, token);
            if (!CanPublishRepositoryFiles(repository, commitHash, generation)) return;
            _repositorySnapshotCache.Set(repositoryIdentity, commitHash, snapshot);
            PublishRepositorySnapshot(repository, commitHash, snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!CanPublishRepositoryFiles(repository, commitHash, generation)) return;
            _repositorySnapshot = [];
            _repositorySnapshotRepository = repository;
            _repositorySnapshotCommit = commitHash;
            ClearRepositoryFilesTree();
            SetRepositoryFilesStatus($"Could not load repository files: {exception.Message}", loading: false);
        }
    }

    private bool CanPublishRepositoryFiles(Repository repository, string commitHash, long generation) =>
        !(_repositorySnapshotCts?.IsCancellationRequested ?? true)
        && generation == Volatile.Read(ref _repositorySnapshotGeneration)
        && IsRepositoryFilesActive
        && ReferenceEquals(repository, _viewModel.Repository)
        && string.Equals(commitHash, _viewModel.SelectedHistoryRow?.Commit.Hash, StringComparison.Ordinal);

    private void PublishRepositorySnapshot(
        Repository repository,
        string commitHash,
        IReadOnlyList<RepositorySnapshotEntry> snapshot)
    {
        _repositorySnapshotRepository = repository;
        _repositorySnapshotCommit = commitHash;
        _repositorySnapshot = snapshot;
        RestoreRepositoryFilesPresentationState(repository, commitHash);
        RebuildRepositoryFilesTree();
        UpdateRepositoryFilesModeSurface();
        if (_repositoryFilesSearchModeName == "Name")
            SetRepositoryFilesStatus(snapshot.Count == 0 ? "This commit has an empty tree." : string.Empty, loading: false);
    }

    private void RebuildRepositoryFilesTree()
    {
        if (_repositoryFilesTree is null) return;
        _repositoryFilesBuildingTree = true;
        try
        {
            _repositoryFilesTree.RootNodes.Clear();
            _repositoryFilesNodes.Clear();
            var query = _repositoryFilesSearchModeName == "Name" ? _repositoryFilesNameQuery : null;
            var roots = RepositorySnapshotTreeNode.Build(_repositorySnapshot, query);
            var searchActive = !string.IsNullOrWhiteSpace(query);
            var state = CurrentRepositoryFilesState(create: true);
            foreach (var root in roots)
                _repositoryFilesTree.RootNodes.Add(CreateRepositoryFilesNode(root, searchActive, state));

            if (state?.SelectedPath is { } selectedPath)
                SelectRepositoryFilesPath(_repositoryFilesTree.RootNodes, selectedPath);

            if (searchActive && roots.Count == 0 && _repositorySnapshot.Count > 0)
                SetRepositoryFilesStatus("No matches", loading: false);
            else if (_repositorySnapshot.Count > 0 && _repositoryFilesSearchModeName == "Name")
                SetRepositoryFilesStatus(string.Empty, loading: false);
        }
        finally
        {
            _repositoryFilesBuildingTree = false;
        }
    }

    private TreeViewNode CreateRepositoryFilesNode(
        RepositorySnapshotTreeNode model,
        bool searchActive,
        RepositoryFilesPresentationState? state)
    {
        var node = new TreeViewNode
        {
            Content = RepositoryFilesDisplayName(model),
            IsExpanded = model.IsDirectory && (searchActive || state?.ExpandedPaths.Contains(model.Path) == true)
        };
        _repositoryFilesNodes[node] = model;
        foreach (var child in model.Children)
            node.Children.Add(CreateRepositoryFilesNode(child, searchActive, state));
        return node;
    }

    private static string RepositoryFilesDisplayName(RepositorySnapshotTreeNode node) => node.Entry?.Kind switch
    {
        RepositorySnapshotEntryKind.Symlink => $"↗ {node.DisplayName}",
        RepositorySnapshotEntryKind.Submodule => $"▣ {node.DisplayName}",
        RepositorySnapshotEntryKind.Unsupported => $"? {node.DisplayName}",
        _ => node.DisplayName
    };

    private void RepositoryFilesSearch_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (_repositoryFilesSearch is null || _repositoryFilesSearchModeName != "Name") return;
        _repositoryFilesNameQuery = _repositoryFilesSearch.Text;
        if (_repositorySnapshotCommit is not null) RebuildRepositoryFilesTree();
    }

    private async void RepositoryFilesSearch_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || _repositoryFilesSearchModeName != "Content") return;
        args.Handled = true;
        await SearchRepositoryContentAsync();
    }

    private void RepositoryFilesSearchMode_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_repositoryFilesSearchMode?.SelectedItem is not string mode) return;
        _repositoryFilesSearchModeName = mode;
        if (mode == "Name")
        {
            _repositoryFilesNameQuery = _repositoryFilesSearch?.Text ?? string.Empty;
            CancelRepositoryContentSearch();
            RebuildRepositoryFilesTree();
        }
        else
        {
            CancelRepositoryContentSearch();
            _repositoryContentMatches = [];
            _repositoryContentCommit = null;
            SetRepositoryFilesStatus("Press Enter to search file contents.", loading: false);
        }
        UpdateRepositoryFilesModeSurface();
    }

    private void UpdateRepositoryFilesModeSurface()
    {
        if (_repositoryFilesTree is null || _repositoryContentResults is null) return;
        var content = _repositoryFilesSearchModeName == "Content";
        _repositoryFilesTree.Visibility = content ? Visibility.Collapsed : Visibility.Visible;
        _repositoryContentResults.Visibility = content ? Visibility.Visible : Visibility.Collapsed;
        _repositoryContentResults.ItemsSource = content
            ? _repositoryContentMatches.Take(RepositoryContentResultLimit).Select(match => new RepositoryContentSearchRow(match)).ToList()
            : null;
    }

    private async Task SearchRepositoryContentAsync()
    {
        if (_repositorySnapshotService is null || _repositoryFilesSearch is null || _repositoryFilesSearchModeName != "Content") return;
        var query = _repositoryFilesSearch.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            _repositoryContentMatches = [];
            _repositoryContentCommit = null;
            UpdateRepositoryFilesModeSurface();
            SetRepositoryFilesStatus("Enter text and press Enter to search file contents.", loading: false);
            return;
        }

        var repository = _viewModel.Repository;
        var commitHash = _viewModel.SelectedHistoryRow?.Commit.Hash;
        if (repository is null || string.IsNullOrWhiteSpace(commitHash)) return;

        CancelRepositoryContentSearch();
        var generation = Interlocked.Increment(ref _repositoryContentSearchGeneration);
        _repositoryContentSearchCts = new CancellationTokenSource();
        var token = _repositoryContentSearchCts.Token;
        _repositoryContentMatches = [];
        _repositoryContentCommit = null;
        UpdateRepositoryFilesModeSurface();
        SetRepositoryFilesStatus("Searching file contents...", loading: true);
        try
        {
            var rawMatches = await _repositorySnapshotService.SearchContentAsync(repository, commitHash, query, token);
            if (!CanPublishRepositoryContent(repository, commitHash, generation)) return;
            var regularPaths = _repositorySnapshot
                .Where(entry => entry.Kind == RepositorySnapshotEntryKind.File)
                .Select(entry => entry.Path)
                .ToHashSet(StringComparer.Ordinal);
            var matches = rawMatches.Where(match => regularPaths.Contains(match.Path)).ToList();
            _repositoryContentMatches = matches;
            _repositoryContentCommit = commitHash;
            UpdateRepositoryFilesModeSurface();
            SetRepositoryFilesStatus(matches.Count switch
            {
                0 => "No matches",
                > RepositoryContentResultLimit => $"Showing first {RepositoryContentResultLimit} of {matches.Count} matches.",
                _ => string.Empty
            }, loading: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!CanPublishRepositoryContent(repository, commitHash, generation)) return;
            _repositoryContentMatches = [];
            _repositoryContentCommit = commitHash;
            UpdateRepositoryFilesModeSurface();
            SetRepositoryFilesStatus($"Content search failed: {exception.Message}", loading: false);
        }
    }

    private bool CanPublishRepositoryContent(Repository repository, string commitHash, long generation) =>
        !(_repositoryContentSearchCts?.IsCancellationRequested ?? true)
        && generation == Volatile.Read(ref _repositoryContentSearchGeneration)
        && IsRepositoryFilesActive
        && _repositoryFilesSearchModeName == "Content"
        && ReferenceEquals(repository, _viewModel.Repository)
        && string.Equals(commitHash, _viewModel.SelectedHistoryRow?.Commit.Hash, StringComparison.Ordinal);

    private void RepositoryFilesTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (sender.SelectedNode is null || !_repositoryFilesNodes.TryGetValue(sender.SelectedNode, out var model)) return;
        if (CurrentRepositoryFilesState(create: true) is { } state) state.SelectedPath = model.Path;
        _repositoryFilesLastSelectedPath = model.Entry is null ? null : model.Path;
    }

    private async void RepositoryFilesTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (SelectedRepositorySnapshotEntry() is not { Kind: RepositorySnapshotEntryKind.File } entry) return;
        await OpenRepositorySnapshotFileAsync(entry, openInEditor: false);
        args.Handled = true;
    }

    private void RepositoryFilesTree_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is not FrameworkElement source || SelectedRepositorySnapshotEntry() is not { } entry) return;
        ShowRepositorySnapshotFileMenu(source, entry);
        args.Handled = true;
    }

    private void RepositoryFilesTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (_repositoryFilesBuildingTree || !string.IsNullOrWhiteSpace(_repositoryFilesNameQuery)) return;
        if (_repositoryFilesNodes.TryGetValue(args.Node, out var model) && model.IsDirectory && CurrentRepositoryFilesState(create: true) is { } state)
            state.ExpandedPaths.Add(model.Path);
    }

    private void RepositoryFilesTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (_repositoryFilesBuildingTree || !string.IsNullOrWhiteSpace(_repositoryFilesNameQuery)) return;
        if (_repositoryFilesNodes.TryGetValue(args.Node, out var model) && model.IsDirectory && CurrentRepositoryFilesState(create: true) is { } state)
            state.ExpandedPaths.Remove(model.Path);
    }

    private RepositorySnapshotEntry? SelectedRepositorySnapshotEntry()
    {
        if (_repositoryFilesTree?.SelectedNode is not { } selected
            || !_repositoryFilesNodes.TryGetValue(selected, out var model))
            return null;
        if (CurrentRepositoryFilesState(create: true) is { } state) state.SelectedPath = model.Path;
        _repositoryFilesLastSelectedPath = model.Entry is null ? null : model.Path;
        return model.Entry;
    }

    private async void RepositoryContentResults_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (_repositoryContentResults?.SelectedItem is not RepositoryContentSearchRow row) return;
        await OpenRepositoryContentMatchAsync(row.Match, openInEditor: false);
        args.Handled = true;
    }

    private async void RepositoryContentResults_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || _repositoryContentResults?.SelectedItem is not RepositoryContentSearchRow row) return;
        args.Handled = true;
        await OpenRepositoryContentMatchAsync(row.Match, openInEditor: false);
    }

    private void RepositoryContentResults_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is not FrameworkElement source || _repositoryContentResults?.SelectedItem is not RepositoryContentSearchRow row) return;
        var entry = _repositorySnapshot.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, row.Match.Path, StringComparison.Ordinal));
        if (entry is null) return;
        ShowRepositorySnapshotFileMenu(source, entry);
        args.Handled = true;
    }

    private async Task OpenRepositoryContentMatchAsync(RepositoryContentSearchMatch match, bool openInEditor)
    {
        if (!string.Equals(_repositoryContentCommit, _viewModel.SelectedHistoryRow?.Commit.Hash, StringComparison.Ordinal)) return;
        var entry = _repositorySnapshot.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, match.Path, StringComparison.Ordinal));
        if (entry is not { Kind: RepositorySnapshotEntryKind.File }) return;
        await OpenRepositorySnapshotFileAsync(entry, openInEditor);
    }

    private void ShowRepositorySnapshotFileMenu(FrameworkElement target, RepositorySnapshotEntry entry)
    {
        var flyout = new MenuFlyout();
        if (entry.Kind == RepositorySnapshotEntryKind.File)
        {
            AddMenuItem(flyout, "Open", true, () => OpenRepositorySnapshotFileAsync(entry, openInEditor: false));
            AddMenuItem(flyout, "Open in editor", _gitToolsService is not null, () => OpenRepositorySnapshotFileAsync(entry, openInEditor: true));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }
        AddMenuItem(flyout, "Copy path", true, () => CopyTextAsync(entry.Path));
        flyout.ShowAt(target);
    }

    private async Task OpenRepositorySnapshotFileAsync(RepositorySnapshotEntry entry, bool openInEditor)
    {
        var repository = _viewModel.Repository;
        var commitHash = _repositorySnapshotCommit;
        if (repository is null || string.IsNullOrWhiteSpace(commitHash)
            || _repositorySnapshotService is null || _fileVersionService is null)
            return;
        if (!ReferenceEquals(repository, _repositorySnapshotRepository)
            || !string.Equals(commitHash, _viewModel.SelectedHistoryRow?.Commit.Hash, StringComparison.Ordinal))
            return;

        try
        {
            var version = await _repositorySnapshotService.ResolveFileVersionAsync(repository, commitHash, entry.Path);
            if (!version.CanOpen)
                throw new NotSupportedException(version.UnavailableReason ?? "This historical entry cannot be opened.");

            if (!openInEditor)
            {
                await OpenResolvedVersionAsync(repository, version, DiffFileSide.Changed);
                return;
            }

            if (_gitToolsService is null)
                throw new InvalidOperationException("Git editor is not available.");
            var materialized = await _fileVersionService.MaterializeAsync(repository, version, DiffFileSide.Changed);
            await _gitToolsService.OpenEditorAsync(repository, materialized.Path);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException exception) when (openInEditor && exception.Message.Contains("editor", StringComparison.OrdinalIgnoreCase))
        {
            await ShowGitEditorUnavailableAsync(exception.Message);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(openInEditor ? "Could not open historical file in editor" : "Could not open historical file", UserFacingFileError(exception));
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
        if (args.Key != VirtualKey.Enter || CommitFilesList.SelectedItem is null || _changesTab is null) return;
        DetailsTabs.SelectedItem = _changesTab;
        args.Handled = true;
    }

    private void SetRepositoryFilesStatus(string text, bool loading)
    {
        if (_repositoryFilesStatus is not null) _repositoryFilesStatus.Text = text;
        if (_repositoryFilesProgress is not null)
        {
            _repositoryFilesProgress.IsActive = loading;
            _repositoryFilesProgress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ClearRepositoryFilesTree()
    {
        if (_repositoryFilesTree is not null) _repositoryFilesTree.RootNodes.Clear();
        _repositoryFilesNodes.Clear();
    }

    private void CancelRepositoryFilesRequests()
    {
        CancelRepositorySnapshotRequest();
        CancelRepositoryContentSearch();
    }

    private void CancelRepositorySnapshotRequest()
    {
        Interlocked.Increment(ref _repositorySnapshotGeneration);
        _repositorySnapshotCts?.Cancel();
        _repositorySnapshotCts?.Dispose();
        _repositorySnapshotCts = null;
    }

    private void CancelRepositoryContentSearch()
    {
        Interlocked.Increment(ref _repositoryContentSearchGeneration);
        _repositoryContentSearchCts?.Cancel();
        _repositoryContentSearchCts?.Dispose();
        _repositoryContentSearchCts = null;
    }

    private void SaveRepositoryFilesPresentationState()
    {
        if (_repositorySnapshotRepository is null || _repositorySnapshotCommit is null) return;
        var state = CurrentRepositoryFilesState(create: true);
        if (state is null) return;
        state.NameQuery = _repositoryFilesNameQuery;
        state.SearchMode = _repositoryFilesSearchModeName;
        _repositoryFilesLastSelectedPath = state.SelectedPath;
        TouchRepositoryFilesState(StateKey(_repositorySnapshotRepository, _repositorySnapshotCommit));
    }

    private void RestoreRepositoryFilesPresentationState(Repository repository, string commitHash)
    {
        var key = StateKey(repository, commitHash);
        if (!_repositoryFilesStates.TryGetValue(key, out var state))
        {
            var selectedPath = _repositoryFilesLastSelectedPath is { } previousPath
                && _repositorySnapshot.Any(entry => string.Equals(entry.Path, previousPath, StringComparison.Ordinal))
                    ? previousPath
                    : null;
            state = new RepositoryFilesPresentationState
            {
                NameQuery = _repositoryFilesNameQuery,
                SearchMode = _repositoryFilesSearchModeName,
                SelectedPath = selectedPath
            };
            _repositoryFilesStates[key] = state;
        }
        TouchRepositoryFilesState(key);

        _repositoryFilesNameQuery = state.NameQuery;
        _repositoryFilesSearchModeName = state.SearchMode;
        if (_repositoryFilesSearch is not null) _repositoryFilesSearch.Text = state.NameQuery;
        if (_repositoryFilesSearchMode is not null) _repositoryFilesSearchMode.SelectedItem = state.SearchMode;
    }

    private RepositoryFilesPresentationState? CurrentRepositoryFilesState(bool create)
    {
        if (_repositorySnapshotRepository is null || _repositorySnapshotCommit is null) return null;
        var key = StateKey(_repositorySnapshotRepository, _repositorySnapshotCommit);
        if (!_repositoryFilesStates.TryGetValue(key, out var state) && create)
        {
            state = new RepositoryFilesPresentationState
            {
                NameQuery = _repositoryFilesNameQuery,
                SearchMode = _repositoryFilesSearchModeName,
                SelectedPath = _repositoryFilesLastSelectedPath
            };
            _repositoryFilesStates[key] = state;
        }
        if (state is not null) TouchRepositoryFilesState(key);
        return state;
    }

    private void TouchRepositoryFilesState(string key)
    {
        if (_repositoryFilesStateLruNodes.Remove(key, out var existing))
            _repositoryFilesStateLru.Remove(existing);
        var node = _repositoryFilesStateLru.AddFirst(key);
        _repositoryFilesStateLruNodes[key] = node;

        while (_repositoryFilesStates.Count > RepositoryFilesStateLimit && _repositoryFilesStateLru.Last is { } oldest)
        {
            _repositoryFilesStateLru.RemoveLast();
            _repositoryFilesStateLruNodes.Remove(oldest.Value);
            _repositoryFilesStates.Remove(oldest.Value);
        }
    }

    private void SelectRepositoryFilesPath(IList<TreeViewNode> nodes, string path)
    {
        if (_repositoryFilesTree is null) return;
        foreach (var node in nodes)
        {
            if (_repositoryFilesNodes.TryGetValue(node, out var model)
                && string.Equals(model.Path, path, StringComparison.Ordinal))
            {
                _repositoryFilesTree.SelectedNode = node;
                return;
            }
            SelectRepositoryFilesPath(node.Children, path);
            if (_repositoryFilesTree.SelectedNode is not null
                && _repositoryFilesNodes.TryGetValue(_repositoryFilesTree.SelectedNode, out var selected)
                && string.Equals(selected.Path, path, StringComparison.Ordinal))
                return;
        }
    }

    private static string RepositoryIdentity(Repository repository) => Path.GetFullPath(repository.GitDirectory);
    private static string StateKey(Repository repository, string commitHash) => RepositoryIdentity(repository) + "\n" + commitHash;

    private sealed class RepositoryFilesPresentationState
    {
        public HashSet<string> ExpandedPaths { get; } = new(StringComparer.Ordinal);
        public string? SelectedPath { get; set; }
        public string NameQuery { get; set; } = string.Empty;
        public string SearchMode { get; set; } = "Name";
    }

    private sealed record RepositoryContentSearchRow(RepositoryContentSearchMatch Match)
    {
        public override string ToString() => $"{Match.Path}\n  {Match.LineNumber}  {Match.Snippet}";
    }
}
