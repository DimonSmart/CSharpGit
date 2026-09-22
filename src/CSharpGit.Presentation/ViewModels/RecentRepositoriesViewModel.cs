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
    private ImageSource? _repositoryImage;
    private string? _repositoryImagePath;
    private ImageSource? _repositoryPreview;
    private string? _repositoryPreviewPath;

    internal RecentRepositoryItem(
        RecentRepositorySettings settings,
        CommitTimeDisplayMode commitTimeDisplayMode,
        Func<RecentRepositoryItem, Task> openAsync,
        Func<RecentRepositoryItem, Task> removeAsync)
    {
        Path = settings.Path;
        DisplayName = settings.DisplayName;
        LastOpenedUtc = settings.LastOpenedUtc;
        LastBranchName = settings.LastBranchName;
        IsAvailable = Directory.Exists(Path);
        TileOpacity = IsAvailable ? 1d : 0.5d;

        var formattedOpenedTime = CommitTimeFormatter.Format(
            LastOpenedUtc,
            commitTimeDisplayMode,
            DateTimeOffset.Now);
        var openedText = $"Last opened {formattedOpenedTime}";
        Metadata = string.IsNullOrWhiteSpace(LastBranchName)
            ? openedText
            : $"{LastBranchName}  ·  {openedText}";
        if (!IsAvailable) Metadata = $"Folder not found  ·  {Metadata}";

        OpenCommand = new AsyncCommand(() => openAsync(this), () => true);
        RemoveCommand = new AsyncCommand(() => removeAsync(this), () => true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }
    public string DisplayName { get; }
    public DateTimeOffset LastOpenedUtc { get; }
    public string? LastBranchName { get; }
    public bool IsAvailable { get; }
    public double TileOpacity { get; }
    public string Metadata { get; }
    public ICommand OpenCommand { get; }
    public ICommand RemoveCommand { get; }
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
    private readonly IAppSettingsService _settings;
    private readonly IRepositoryImageService _repositoryImageService;
    private readonly Func<RecentRepositoryItem, Task> _openRecentAsync;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource _imageLoadCancellation = new();
    private bool _imageLoadingStarted;
    private bool _disposed;

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
        Reload();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RecentRepositoryItem> RecentRepositories { get; } = [];

    public ICommand OpenRepositoryCommand { get; }

    public ICommand CreateRepositoryCommand { get; }

    public ICommand RemoveUnavailableRepositoriesCommand { get; }

    public bool HasUnavailableRepositories => RecentRepositories.Any(item => !item.IsAvailable);

    internal void StartImageLoading()
    {
        if (_disposed) return;
        _imageLoadingStarted = true;
        ScheduleImageRefreshes();
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
            Reload();
            return;
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed) Reload();
        });
    }

    private void Reload()
    {
        _imageLoadCancellation.Cancel();
        _imageLoadCancellation.Dispose();
        _imageLoadCancellation = new CancellationTokenSource();

        RecentRepositories.Clear();
        foreach (var settings in _settings.RecentRepositories
                     .OrderByDescending(settings => Directory.Exists(settings.Path)))
        {
            var item = new RecentRepositoryItem(
                settings,
                _settings.CommitTimeDisplayMode,
                _openRecentAsync,
                RemoveAsync);
            var cached = _repositoryImageService.GetCachedState(item.Path);
            item.SetRepositoryVisual(cached);
            item.NeedsImageRefresh = cached.ShouldRefresh;
            RecentRepositories.Add(item);
        }

        OnPropertyChanged(nameof(HasUnavailableRepositories));

        if (_imageLoadingStarted)
            ScheduleImageRefreshes();
    }

    private void ScheduleImageRefreshes()
    {
        var cancellationToken = _imageLoadCancellation.Token;
        foreach (var item in RecentRepositories.Where(item => item.NeedsImageRefresh))
            _ = LoadImageAsync(item, cancellationToken);
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
    }

    private async Task RemoveUnavailableRepositoriesAsync()
    {
        var unavailablePaths = RecentRepositories
            .Where(item => !item.IsAvailable)
            .Select(item => item.Path)
            .ToArray();

        foreach (var path in unavailablePaths)
            await _settings.RemoveRecentRepositoryAsync(path);
    }

    private Task RemoveAsync(RecentRepositoryItem item) => _settings.RemoveRecentRepositoryAsync(item.Path);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
