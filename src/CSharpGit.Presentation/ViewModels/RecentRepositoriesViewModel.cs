using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CSharpGit.Presentation.ViewModels;

public sealed class RecentRepositoryItem : INotifyPropertyChanged
{
    private readonly AsyncCommand _moveEarlierCommand;
    private readonly AsyncCommand _moveLaterCommand;
    private ImageSource? _repositoryImage;
    private string? _repositoryImagePath;
    private ImageSource? _repositoryPreview;
    private string? _repositoryPreviewPath;
    private string _displayName = string.Empty;
    private DateTimeOffset _lastOpenedUtc;
    private string? _lastBranchName;
    private bool _isAvailable;
    private string _metadata = string.Empty;
    private bool _isPinned;
    private int? _pinnedOrder;
    private string? _groupId;
    private bool _canMoveEarlier;
    private bool _canMoveLater;

    internal RecentRepositoryItem(
        RecentRepositorySettings settings,
        CommitTimeDisplayMode commitTimeDisplayMode,
        Func<RecentRepositoryItem, Task> openAsync,
        Func<RecentRepositoryItem, Task> openFolderAsync,
        Func<RecentRepositoryItem, Task> copyPathAsync,
        string openFolderActionLabel,
        Func<RecentRepositoryItem, Task> removeAsync,
        Func<RecentRepositoryItem, Task> togglePinnedAsync,
        Func<RecentRepositoryItem, Task> moveEarlierAsync,
        Func<RecentRepositoryItem, Task> moveLaterAsync)
    {
        Path = settings.Path;
        _isAvailable = Directory.Exists(Path);
        OpenCommand = new AsyncCommand(() => openAsync(this), () => true);
        OpenFolderCommand = new AsyncCommand(() => openFolderAsync(this), () => true);
        CopyPathCommand = new AsyncCommand(() => copyPathAsync(this), () => true);
        OpenFolderActionLabel = openFolderActionLabel;
        RemoveCommand = new AsyncCommand(() => removeAsync(this), () => true);
        PinCommand = new AsyncCommand(() => togglePinnedAsync(this), () => true);
        _moveEarlierCommand = new AsyncCommand(() => moveEarlierAsync(this), () => _canMoveEarlier);
        _moveLaterCommand = new AsyncCommand(() => moveLaterAsync(this), () => _canMoveLater);
        UpdateSettings(settings, commitTimeDisplayMode);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }
    public string DisplayName => _displayName;
    public DateTimeOffset LastOpenedUtc => _lastOpenedUtc;
    public string? LastBranchName => _lastBranchName;
    public bool IsAvailable => _isAvailable;
    public double TileOpacity => IsAvailable ? 1d : 0.5d;
    public string Metadata => _metadata;
    public string TileDescription => $"{DisplayName}\n{Path}\n{Metadata}";
    public bool IsPinned => _isPinned;
    public int? PinnedOrder => _pinnedOrder;
    public string? GroupId => _groupId;
    public string PinActionLabel => IsPinned ? "Unpin repository" : "Pin repository";
    public string PinGlyph => IsPinned ? "\uE718" : "\uE77A";
    public double PinIndicatorOpacity => IsPinned ? 1d : 0.85d;
    public bool CanMoveEarlier => _canMoveEarlier;
    public bool CanMoveLater => _canMoveLater;
    public string OpenFolderActionLabel { get; }
    public ICommand OpenCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand PinCommand { get; }
    public ICommand MoveEarlierCommand => _moveEarlierCommand;
    public ICommand MoveLaterCommand => _moveLaterCommand;
    public ImageSource? RepositoryImage => _repositoryImage;
    public string? RepositoryImagePath => _repositoryImagePath;
    public double RepositoryImageOpacity => _repositoryImage is null ? 0d : 1d;
    public double RepositoryGlyphOpacity => _repositoryImage is null && _repositoryPreview is null ? 1d : 0d;
    public ImageSource? RepositoryPreview => _repositoryPreview;
    public string? RepositoryPreviewPath => _repositoryPreviewPath;
    public double RepositoryPreviewOpacity => _repositoryPreview is null ? 0d : 1d;

    internal bool NeedsImageRefresh { get; set; }

    internal void UpdateSettings(
        RecentRepositorySettings settings,
        CommitTimeDisplayMode commitTimeDisplayMode)
    {
        if (!string.Equals(Path, settings.Path, RecentRepositoriesViewModel.PathComparison))
            throw new InvalidOperationException("A recent repository item cannot change its path.");

        if (!string.Equals(_displayName, settings.DisplayName, StringComparison.Ordinal))
        {
            _displayName = settings.DisplayName;
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(TileDescription));
        }

        if (_lastOpenedUtc != settings.LastOpenedUtc)
        {
            _lastOpenedUtc = settings.LastOpenedUtc;
            OnPropertyChanged(nameof(LastOpenedUtc));
        }

        if (!string.Equals(_lastBranchName, settings.LastBranchName, StringComparison.Ordinal))
        {
            _lastBranchName = settings.LastBranchName;
            OnPropertyChanged(nameof(LastBranchName));
        }

        if (_isPinned != settings.IsPinned)
        {
            _isPinned = settings.IsPinned;
            OnPropertyChanged(nameof(IsPinned));
            OnPropertyChanged(nameof(PinActionLabel));
            OnPropertyChanged(nameof(PinGlyph));
            OnPropertyChanged(nameof(PinIndicatorOpacity));
        }

        if (_pinnedOrder != settings.PinnedOrder)
        {
            _pinnedOrder = settings.PinnedOrder;
            OnPropertyChanged(nameof(PinnedOrder));
        }

        if (!string.Equals(_groupId, settings.GroupId, StringComparison.Ordinal))
        {
            _groupId = settings.GroupId;
            OnPropertyChanged(nameof(GroupId));
        }

        UpdateMetadata(commitTimeDisplayMode);
    }

    internal void SetAvailability(
        bool isAvailable,
        CommitTimeDisplayMode commitTimeDisplayMode)
    {
        if (_isAvailable == isAvailable) return;

        _isAvailable = isAvailable;
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(TileOpacity));
        UpdateMetadata(commitTimeDisplayMode);
    }

    internal void SetMoveCapabilities(bool canMoveEarlier, bool canMoveLater)
    {
        if (_canMoveEarlier != canMoveEarlier)
        {
            _canMoveEarlier = canMoveEarlier;
            OnPropertyChanged(nameof(CanMoveEarlier));
            _moveEarlierCommand.RaiseCanExecuteChanged();
        }

        if (_canMoveLater != canMoveLater)
        {
            _canMoveLater = canMoveLater;
            OnPropertyChanged(nameof(CanMoveLater));
            _moveLaterCommand.RaiseCanExecuteChanged();
        }
    }

    internal void SetRepositoryVisual(RepositoryImageCacheState state)
    {
        if (state.Kind == RepositoryImageKind.Icon)
        {
            SetRepositoryPreviewPath(null);
            SetRepositoryImagePath(state.ImagePath);
            return;
        }

        if (state.Kind == RepositoryImageKind.Preview)
        {
            SetRepositoryImagePath(null);
            SetRepositoryPreviewPath(state.ImagePath);
            return;
        }

        SetRepositoryPreviewPath(null);
        SetRepositoryImagePath(null);
    }

    internal void SetRepositoryImagePath(string? imagePath)
    {
        var (image, normalizedPath) = LoadImage(imagePath);
        if (string.Equals(_repositoryImagePath, normalizedPath, StringComparison.Ordinal)
            && (_repositoryImage is null) == (image is null))
            return;

        _repositoryImagePath = normalizedPath;
        _repositoryImage = image;
        OnPropertyChanged(nameof(RepositoryImagePath));
        OnPropertyChanged(nameof(RepositoryImage));
        OnPropertyChanged(nameof(RepositoryImageOpacity));
        OnPropertyChanged(nameof(RepositoryGlyphOpacity));
    }

    internal void SetRepositoryPreviewPath(string? imagePath)
    {
        var (image, normalizedPath) = LoadImage(imagePath);
        if (string.Equals(_repositoryPreviewPath, normalizedPath, StringComparison.Ordinal)
            && (_repositoryPreview is null) == (image is null))
            return;

        _repositoryPreviewPath = normalizedPath;
        _repositoryPreview = image;
        OnPropertyChanged(nameof(RepositoryPreviewPath));
        OnPropertyChanged(nameof(RepositoryPreview));
        OnPropertyChanged(nameof(RepositoryPreviewOpacity));
        OnPropertyChanged(nameof(RepositoryGlyphOpacity));
    }

    private void UpdateMetadata(CommitTimeDisplayMode commitTimeDisplayMode)
    {
        var formattedOpenedTime = CommitTimeFormatter.Format(
            LastOpenedUtc,
            commitTimeDisplayMode,
            DateTimeOffset.Now);
        var openedText = $"Last opened {formattedOpenedTime}";
        var metadata = string.IsNullOrWhiteSpace(LastBranchName)
            ? openedText
            : $"{LastBranchName}  ·  {openedText}";
        if (!IsAvailable) metadata = $"Folder not found  ·  {metadata}";

        if (string.Equals(_metadata, metadata, StringComparison.Ordinal)) return;
        _metadata = metadata;
        OnPropertyChanged(nameof(Metadata));
        OnPropertyChanged(nameof(TileDescription));
    }

    private static (ImageSource? Image, string? Path) LoadImage(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return (null, null);

        try
        {
            var normalizedPath = System.IO.Path.GetFullPath(imagePath);
            var imageUri = new UriBuilder(Uri.UriSchemeFile, string.Empty)
            {
                Path = normalizedPath
            }.Uri;
            return (new BitmapImage(imageUri), normalizedPath);
        }
        catch
        {
            return (null, null);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}


public sealed class RepositoryGroupViewModel : INotifyPropertyChanged
{
    private string _name;
    private int _order;
    private bool _isExpanded;
    private bool _isSearchActive;
    private bool _isVisible = true;
    private bool _effectiveIsExpanded;
    private int _repositoryCount;
    private bool _canMoveUp;
    private bool _canMoveDown;

    internal RepositoryGroupViewModel(RepositoryGroupSettings settings)
    {
        Id = settings.Id;
        _name = settings.Name;
        _order = settings.Order;
        _isExpanded = settings.IsExpanded;
        _effectiveIsExpanded = settings.IsExpanded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; }
    public string Name => _name;
    public int Order => _order;
    public bool IsExpanded => _isExpanded;
    public bool EffectiveIsExpanded => _effectiveIsExpanded;
    public string ExpandGlyph => EffectiveIsExpanded ? "\uE70D" : "\uE76C";
    public bool CanToggleExpanded => !_isSearchActive;
    public bool IsVisible => _isVisible;
    public int RepositoryCount => _repositoryCount;
    public string Heading => $"{Name} ({RepositoryCount})";
    public string ExpansionActionLabel => $"{(EffectiveIsExpanded ? "Collapse" : "Expand")} {Heading}";
    public string ActionsLabel => $"Actions for {Name} group";
    public bool CanDelete => Id != "favorites";
    public bool CanMoveUp => _canMoveUp;
    public bool CanMoveDown => _canMoveDown;
    public ObservableCollection<RecentRepositoryItem> Repositories { get; } = [];

    internal void Update(RepositoryGroupSettings settings, bool searching, int count,
        IReadOnlyList<RecentRepositoryItem> items, bool canMoveUp, bool canMoveDown)
    {
        if (_name != settings.Name)
        {
            _name = settings.Name;
            Notify(nameof(Name)); Notify(nameof(Heading)); Notify(nameof(ActionsLabel));
            Notify(nameof(ExpansionActionLabel));
        }
        if (_order != settings.Order) { _order = settings.Order; Notify(nameof(Order)); }
        if (_isExpanded != settings.IsExpanded) { _isExpanded = settings.IsExpanded; Notify(nameof(IsExpanded)); }
        if (_isSearchActive != searching)
        {
            _isSearchActive = searching;
            Notify(nameof(CanToggleExpanded));
        }
        var effective = searching ? count > 0 : _isExpanded;
        if (_effectiveIsExpanded != effective)
        {
            _effectiveIsExpanded = effective;
            Notify(nameof(EffectiveIsExpanded)); Notify(nameof(ExpandGlyph));
            Notify(nameof(ExpansionActionLabel));
        }
        var visible = !searching || count > 0;
        if (_isVisible != visible) { _isVisible = visible; Notify(nameof(IsVisible)); }
        if (_repositoryCount != count)
        {
            _repositoryCount = count;
            Notify(nameof(RepositoryCount)); Notify(nameof(Heading));
            Notify(nameof(ExpansionActionLabel));
        }
        if (_canMoveUp != canMoveUp) { _canMoveUp = canMoveUp; Notify(nameof(CanMoveUp)); }
        if (_canMoveDown != canMoveDown) { _canMoveDown = canMoveDown; Notify(nameof(CanMoveDown)); }
        RecentRepositoriesViewModel.SyncCollection(Repositories, items);
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RecentRepositoriesViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IAppSettingsService _settings;
    private readonly IRepositoryImageService _repositoryImageService;
    private readonly Func<RecentRepositoryItem, Task> _openRecentAsync;
    private readonly Func<RecentRepositoryItem, Task> _openRecentFolderAsync;
    private readonly Func<RecentRepositoryItem, Task> _copyRecentPathAsync;
    private readonly string _openFolderActionLabel;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Dictionary<string, RecentRepositoryItem> _itemsByPath = new(PathComparer);
    private readonly Dictionary<string, RepositoryGroupViewModel> _groupsById = new(StringComparer.Ordinal);
    private readonly HashSet<string> _imageLoadsInProgress = new(PathComparer);
    private readonly CancellationTokenSource _imageLoadCancellation = new();
    private bool _imageLoadingStarted;
    private bool _disposed;
    private string _searchText = string.Empty;
    private RepositoryLauncherSettingsSnapshot _reconciledSnapshot = null!;

    internal RecentRepositoriesViewModel(
        IAppSettingsService settings,
        IRepositoryImageService repositoryImageService,
        Func<RecentRepositoryItem, Task> openRecentAsync,
        Func<RecentRepositoryItem, Task> openRecentFolderAsync,
        Func<RecentRepositoryItem, Task> copyRecentPathAsync,
        string openFolderActionLabel,
        Func<Task> openRepositoryAsync,
        Func<Task> cloneRepositoryAsync,
        Func<Task> createRepositoryAsync,
        DispatcherQueue dispatcherQueue)
    {
        _settings = settings;
        _repositoryImageService = repositoryImageService;
        _openRecentAsync = openRecentAsync;
        _openRecentFolderAsync = openRecentFolderAsync;
        _copyRecentPathAsync = copyRecentPathAsync;
        _openFolderActionLabel = openFolderActionLabel;
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        OpenRepositoryCommand = new AsyncCommand(openRepositoryAsync, () => true);
        CloneRepositoryCommand = new AsyncCommand(cloneRepositoryAsync, () => true);
        CreateRepositoryCommand = new AsyncCommand(createRepositoryAsync, () => true);
        RemoveUnavailableRepositoriesCommand = new AsyncCommand(RemoveUnavailableRepositoriesAsync, () => true);
        _settings.Changed += Settings_Changed;
        Reconcile();
    }

    internal static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RepositoryGroupViewModel> Groups { get; } = [];

    public ObservableCollection<RecentRepositoryItem> RecentRepositories { get; } = [];

    public ICommand OpenRepositoryCommand { get; }

    public ICommand CloneRepositoryCommand { get; }

    public ICommand CreateRepositoryCommand { get; }

    public ICommand RemoveUnavailableRepositoriesCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_searchText, next, StringComparison.Ordinal)) return;
            _searchText = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSearchActive));
            RebuildProjection();
        }
    }

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchText);

    public bool HasPinnedRepositories => Groups.Any(group => group.Repositories.Count > 0);

    public bool HasRecentRepositories => RecentRepositories.Count > 0;

    public bool HasSearchResults => HasPinnedRepositories || HasRecentRepositories;

    public bool ShowNoSearchResults => IsSearchActive && !HasSearchResults;

    public bool HasUnavailableRepositories => _itemsByPath.Values.Any(item => !item.IsAvailable);

    internal void StartImageLoading()
    {
        if (_disposed) return;
        _imageLoadingStarted = true;
        ScheduleImageRefreshes();
    }

    public Task<RepositoryGroupSettings> CreateGroupAsync(string name) => _settings.CreateRepositoryGroupAsync(name);
    public Task RenameGroupAsync(string id, string name) => _settings.RenameRepositoryGroupAsync(id, name);
    public Task DeleteGroupAsync(string id) => _settings.DeleteRepositoryGroupAsync(id);
    public Task MoveGroupAsync(string id, int newIndex) => _settings.MoveRepositoryGroupAsync(id, newIndex);
    public Task SetGroupExpandedAsync(string id, bool expanded) => _settings.SetRepositoryGroupExpandedAsync(id, expanded);
    public Task PinToGroupAsync(RecentRepositoryItem item, string id) => _settings.MoveRecentRepositoryToGroupAsync(item.Path, id);
    public Task UnpinAsync(RecentRepositoryItem item) => _settings.SetRecentRepositoryPinnedAsync(item.Path, false);
    public Task RemoveRepositoryAsync(RecentRepositoryItem item) => _settings.RemoveRecentRepositoryAsync(item.Path);
    public Task TogglePinAsync(RecentRepositoryItem item) => _settings.SetRecentRepositoryPinnedAsync(item.Path, !item.IsPinned);

    internal Task MovePinnedRepositoryAsync(RecentRepositoryItem item, int newIndex)
    {
        if (_disposed || IsSearchActive || !item.IsPinned || item.PinnedOrder is null)
            return Task.CompletedTask;

        return _settings.MovePinnedRepositoryAsync(item.Path, newIndex);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Changed -= Settings_Changed;
        _imageLoadCancellation.Cancel();
        _imageLoadCancellation.Dispose();
    }

    private void Settings_Changed(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (_dispatcherQueue.HasThreadAccess)
        {
            Reconcile();
            return;
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed) Reconcile();
        });
    }

    private void Reconcile()
    {
        var snapshot = _settings.RepositoryLauncherSnapshot;
        _reconciledSnapshot = snapshot;
        var currentSettings = snapshot.Repositories;
        var currentPaths = currentSettings.Select(settings => settings.Path).ToHashSet(PathComparer);

        foreach (var path in _itemsByPath.Keys.Where(path => !currentPaths.Contains(path)).ToArray())
            _itemsByPath.Remove(path);

        foreach (var settings in currentSettings)
        {
            if (_itemsByPath.TryGetValue(settings.Path, out var existing))
            {
                existing.UpdateSettings(settings, _settings.CommitTimeDisplayMode);
                continue;
            }

            var item = new RecentRepositoryItem(
                settings,
                _settings.CommitTimeDisplayMode,
                _openRecentAsync,
                _openRecentFolderAsync,
                _copyRecentPathAsync,
                _openFolderActionLabel,
                RemoveAsync,
                TogglePinnedAsync,
                MoveEarlierAsync,
                MoveLaterAsync);
            var cached = _repositoryImageService.GetCachedState(item.Path);
            item.SetRepositoryVisual(cached);
            item.NeedsImageRefresh = cached.ShouldRefresh;
            _itemsByPath.Add(item.Path, item);
        }

        RebuildProjection();
        OnPropertyChanged(nameof(HasUnavailableRepositories));

        if (_imageLoadingStarted)
            ScheduleImageRefreshes();
    }

    private void RebuildProjection()
    {
        var snapshot = _reconciledSnapshot;
        var projection = RecentRepositoriesProjectionBuilder.Build(snapshot.Groups, snapshot.Repositories, SearchText);
        var visibleGroups = projection.Groups.ToDictionary(group => group.Settings.Id, StringComparer.Ordinal);
        var desiredGroups = new List<RepositoryGroupViewModel>();
        foreach (var settings in snapshot.Groups.OrderBy(group => group.Order))
        {
            if (!_groupsById.TryGetValue(settings.Id, out var group))
            {
                group = new RepositoryGroupViewModel(settings);
                _groupsById.Add(settings.Id, group);
            }
            var matched = visibleGroups.TryGetValue(settings.Id, out var projected)
                ? projected.Repositories : [];
            var items = matched.Select(item => _itemsByPath[item.Path]).ToArray();
            group.Update(settings, IsSearchActive, items.Length, items,
                settings.Order > 0, settings.Order < snapshot.Groups.Count - 1);
            desiredGroups.Add(group);
        }

        foreach (var id in _groupsById.Keys.Where(id => desiredGroups.All(group => group.Id != id)).ToArray())
            _groupsById.Remove(id);
        SyncGroups(Groups, desiredGroups);
        SyncCollection(RecentRepositories,
            projection.Recent.Select(settings => _itemsByPath[settings.Path]).ToArray());

        foreach (var item in _itemsByPath.Values)
            item.SetMoveCapabilities(false, false);

        if (!IsSearchActive)
        {
            var allGroups = RecentRepositoriesProjectionBuilder.Build(snapshot.Groups, snapshot.Repositories, null).Groups;
            foreach (var group in allGroups)
            {
                for (var index = 0; index < group.Repositories.Count; index++)
                {
                    var item = _itemsByPath[group.Repositories[index].Path];
                    item.SetMoveCapabilities(index > 0, index < group.Repositories.Count - 1);
                }
            }
        }

        OnPropertyChanged(nameof(HasPinnedRepositories));
        OnPropertyChanged(nameof(HasRecentRepositories));
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(ShowNoSearchResults));
    }

    private static void SyncGroups(
        ObservableCollection<RepositoryGroupViewModel> collection,
        IReadOnlyList<RepositoryGroupViewModel> desired)
    {
        for (var index = collection.Count - 1; index >= 0; index--)
            if (!desired.Contains(collection[index])) collection.RemoveAt(index);
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < collection.Count && ReferenceEquals(collection[index], desired[index])) continue;
            var oldIndex = collection.IndexOf(desired[index]);
            if (oldIndex >= 0) collection.Move(oldIndex, index);
            else collection.Insert(index, desired[index]);
        }
    }

    internal static void SyncCollection(
        ObservableCollection<RecentRepositoryItem> collection,
        IReadOnlyList<RecentRepositoryItem> desired)
    {
        var desiredSet = desired.ToHashSet();
        for (var index = collection.Count - 1; index >= 0; index--)
        {
            if (!desiredSet.Contains(collection[index]))
                collection.RemoveAt(index);
        }

        for (var index = 0; index < desired.Count; index++)
        {
            if (index < collection.Count && ReferenceEquals(collection[index], desired[index]))
                continue;

            var existingIndex = collection.IndexOf(desired[index]);
            if (existingIndex >= 0)
                collection.Move(existingIndex, index);
            else
                collection.Insert(index, desired[index]);
        }
    }

    private void ScheduleImageRefreshes()
    {
        var cancellationToken = _imageLoadCancellation.Token;
        foreach (var item in _itemsByPath.Values.Where(item => item.NeedsImageRefresh))
        {
            lock (_imageLoadsInProgress)
            {
                if (!_imageLoadsInProgress.Add(item.Path)) continue;
            }

            _ = LoadImageAsync(item, cancellationToken);
        }
    }

    private async Task LoadImageAsync(
        RecentRepositoryItem item,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            await _repositoryImageService.ResolveAsync(
                item.Path,
                cancellationToken);

            if (_disposed || cancellationToken.IsCancellationRequested) return;
            if (!_itemsByPath.TryGetValue(item.Path, out var current)
                || !ReferenceEquals(current, item))
                return;

            var resolved = _repositoryImageService.GetCachedState(item.Path);
            item.NeedsImageRefresh = false;
            item.SetRepositoryVisual(resolved);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Could not resolve repository image for '{item.Path}': {exception}");
        }
        finally
        {
            lock (_imageLoadsInProgress)
                _imageLoadsInProgress.Remove(item.Path);
        }
    }

    private async Task RemoveUnavailableRepositoriesAsync()
    {
        var unavailableItems = _itemsByPath.Values
            .Where(item => !item.IsAvailable)
            .ToArray();

        foreach (var item in unavailableItems)
        {
            if (Directory.Exists(item.Path))
            {
                item.SetAvailability(true, _settings.CommitTimeDisplayMode);
                continue;
            }

            await _settings.RemoveRecentRepositoryAsync(item.Path);
        }

        OnPropertyChanged(nameof(HasUnavailableRepositories));
    }

    private Task RemoveAsync(RecentRepositoryItem item) =>
        _settings.RemoveRecentRepositoryAsync(item.Path);

    private Task TogglePinnedAsync(RecentRepositoryItem item) => TogglePinAsync(item);

    private Task MoveEarlierAsync(RecentRepositoryItem item) =>
        item.PinnedOrder is > 0 && !IsSearchActive
            ? _settings.MovePinnedRepositoryAsync(item.Path, item.PinnedOrder.Value - 1)
            : Task.CompletedTask;

    private Task MoveLaterAsync(RecentRepositoryItem item) =>
        item.PinnedOrder is not null && !IsSearchActive
            ? _settings.MovePinnedRepositoryAsync(item.Path, item.PinnedOrder.Value + 1)
            : Task.CompletedTask;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
