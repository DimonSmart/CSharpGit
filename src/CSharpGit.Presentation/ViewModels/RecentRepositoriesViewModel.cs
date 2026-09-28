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
    private bool _canMoveEarlier;
    private bool _canMoveLater;

    internal RecentRepositoryItem(
        RecentRepositorySettings settings,
        CommitTimeDisplayMode commitTimeDisplayMode,
        Func<RecentRepositoryItem, Task> openAsync,
        Func<RecentRepositoryItem, Task> removeAsync,
        Func<RecentRepositoryItem, Task> togglePinnedAsync,
        Func<RecentRepositoryItem, Task> moveEarlierAsync,
        Func<RecentRepositoryItem, Task> moveLaterAsync)
    {
        Path = settings.Path;
        _isAvailable = Directory.Exists(Path);
        OpenCommand = new AsyncCommand(() => openAsync(this), () => true);
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
    public bool IsPinned => _isPinned;
    public int? PinnedOrder => _pinnedOrder;
    public string PinActionLabel => IsPinned ? "Unpin repository" : "Pin repository";
    public double PinIndicatorOpacity => IsPinned ? 1d : 0.55d;
    public bool CanMoveEarlier => _canMoveEarlier;
    public bool CanMoveLater => _canMoveLater;
    public ICommand OpenCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand PinCommand { get; }
    public ICommand MoveEarlierCommand => _moveEarlierCommand;
    public ICommand MoveLaterCommand => _moveLaterCommand;
    public ImageSource? RepositoryImage => _repositoryImage;
    public string? RepositoryImagePath => _repositoryImagePath;
    public double RepositoryImageOpacity => _repositoryImage is null ? 0d : 1d;
    public double RepositoryGlyphOpacity => _repositoryImage is null ? 1d : 0d;
    public ImageSource? RepositoryPreview => _repositoryPreview;
    public string? RepositoryPreviewPath => _repositoryPreviewPath;
    public int RepositoryColumnSpan => _repositoryPreview is null ? 1 : 2;
    public Visibility RepositoryCompactLayoutVisibility =>
        _repositoryPreview is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RepositoryPreviewLayoutVisibility =>
        _repositoryPreview is null ? Visibility.Collapsed : Visibility.Visible;

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
            OnPropertyChanged(nameof(PinIndicatorOpacity));
        }

        if (_pinnedOrder != settings.PinnedOrder)
        {
            _pinnedOrder = settings.PinnedOrder;
            OnPropertyChanged(nameof(PinnedOrder));
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
        OnPropertyChanged(nameof(RepositoryColumnSpan));
        OnPropertyChanged(nameof(RepositoryCompactLayoutVisibility));
        OnPropertyChanged(nameof(RepositoryPreviewLayoutVisibility));
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

public sealed class RecentRepositoriesViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IAppSettingsService _settings;
    private readonly IRepositoryImageService _repositoryImageService;
    private readonly Func<RecentRepositoryItem, Task> _openRecentAsync;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Dictionary<string, RecentRepositoryItem> _itemsByPath = new(PathComparer);
    private readonly HashSet<string> _imageLoadsInProgress = new(PathComparer);
    private readonly CancellationTokenSource _imageLoadCancellation = new();
    private bool _imageLoadingStarted;
    private bool _disposed;
    private string _searchText = string.Empty;

    internal RecentRepositoriesViewModel(
        IAppSettingsService settings,
        IRepositoryImageService repositoryImageService,
        Func<RecentRepositoryItem, Task> openRecentAsync,
        Func<Task> openRepositoryAsync,
        Func<Task> createRepositoryAsync,
        DispatcherQueue dispatcherQueue)
    {
        _settings = settings;
        _repositoryImageService = repositoryImageService;
        _openRecentAsync = openRecentAsync;
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        OpenRepositoryCommand = new AsyncCommand(openRepositoryAsync, () => true);
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

    public ObservableCollection<RecentRepositoryItem> PinnedRepositories { get; } = [];

    public ObservableCollection<RecentRepositoryItem> RecentRepositories { get; } = [];

    public ICommand OpenRepositoryCommand { get; }

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

    public bool HasPinnedRepositories => PinnedRepositories.Count > 0;

    public bool HasRecentRepositories => RecentRepositories.Count > 0;

    public bool HasSearchResults => HasPinnedRepositories || HasRecentRepositories;

    public bool ShowNoSearchResults => IsSearchActive && _itemsByPath.Count > 0 && !HasSearchResults;

    public bool HasUnavailableRepositories => _itemsByPath.Values.Any(item => !item.IsAvailable);

    internal void StartImageLoading()
    {
        if (_disposed) return;
        _imageLoadingStarted = true;
        ScheduleImageRefreshes();
    }

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
        var currentSettings = _settings.RecentRepositories;
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
        var projection = RecentRepositoriesProjectionBuilder.Build(_settings.RecentRepositories, SearchText);
        SyncCollection(
            PinnedRepositories,
            projection.Pinned.Select(settings => _itemsByPath[settings.Path]).ToArray());
        SyncCollection(
            RecentRepositories,
            projection.Recent.Select(settings => _itemsByPath[settings.Path]).ToArray());

        foreach (var item in _itemsByPath.Values)
            item.SetMoveCapabilities(false, false);

        if (!IsSearchActive)
        {
            var allPinned = RecentRepositoriesProjectionBuilder.Build(_settings.RecentRepositories, null).Pinned;
            for (var index = 0; index < allPinned.Count; index++)
            {
                var item = _itemsByPath[allPinned[index].Path];
                item.SetMoveCapabilities(index > 0, index < allPinned.Count - 1);
            }
        }

        OnPropertyChanged(nameof(HasPinnedRepositories));
        OnPropertyChanged(nameof(HasRecentRepositories));
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(ShowNoSearchResults));
    }

    private static void SyncCollection(
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

    private Task TogglePinnedAsync(RecentRepositoryItem item) =>
        _settings.SetRecentRepositoryPinnedAsync(item.Path, !item.IsPinned);

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
