using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CSharpGit.Presentation.ViewModels;

public sealed class RecentRepositoryItem : INotifyPropertyChanged
{
    private ImageSource? _repositoryImage;
    private string? _repositoryImagePath;

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

        var openedText = CommitTimeFormatter.Format(
            LastOpenedUtc,
            commitTimeDisplayMode,
            DateTimeOffset.Now);
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

    internal bool NeedsImageRefresh { get; set; }

    internal void SetRepositoryImagePath(string? imagePath)
    {
        ImageSource? image = null;
        string? normalizedPath = null;

        if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
        {
            try
            {
                normalizedPath = System.IO.Path.GetFullPath(imagePath);
                var imageUri = new UriBuilder(Uri.UriSchemeFile, string.Empty)
                {
                    Path = normalizedPath
                }.Uri;
                image = new BitmapImage(imageUri);
            }
            catch
            {
                normalizedPath = null;
            }
        }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RecentRepositoriesViewModel : IDisposable
{
    private readonly IAppSettingsService _settings;
    private readonly IRepositoryImageService _repositoryImageService;
    private readonly Func<RecentRepositoryItem, Task> _openRecentAsync;
    private CancellationTokenSource _imageLoadCancellation = new();
    private bool _imageLoadingStarted;
    private bool _disposed;

    internal RecentRepositoriesViewModel(
        IAppSettingsService settings,
        Func<RecentRepositoryItem, Task> openRecentAsync,
        Func<Task> openRepositoryAsync)
        : this(
            settings,
            CSharpGit.Presentation.RepositoryImageServices.Current,
            openRecentAsync,
            openRepositoryAsync)
    {
    }

    internal RecentRepositoriesViewModel(
        IAppSettingsService settings,
        IRepositoryImageService repositoryImageService,
        Func<RecentRepositoryItem, Task> openRecentAsync,
        Func<Task> openRepositoryAsync)
    {
        _settings = settings;
        _repositoryImageService = repositoryImageService;
        _openRecentAsync = openRecentAsync;
        OpenRepositoryCommand = new AsyncCommand(openRepositoryAsync, () => true);
        _settings.Changed += Settings_Changed;
        Reload();
    }

    public ObservableCollection<RecentRepositoryItem> RecentRepositories { get; } = [];

    public ICommand OpenRepositoryCommand { get; }

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
        if (!_disposed) Reload();
    }

    private void Reload()
    {
        _imageLoadCancellation.Cancel();
        _imageLoadCancellation.Dispose();
        _imageLoadCancellation = new CancellationTokenSource();

        RecentRepositories.Clear();
        foreach (var settings in _settings.RecentRepositories)
        {
            var item = new RecentRepositoryItem(
                settings,
                _settings.CommitTimeDisplayMode,
                _openRecentAsync,
                RemoveAsync);
            var cached = _repositoryImageService.GetCachedState(item.Path);
            item.SetRepositoryImagePath(cached.ImagePath);
            item.NeedsImageRefresh = cached.ShouldRefresh;
            RecentRepositories.Add(item);
        }

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
            // Do not start filesystem/network resolving while the start screen is
            // still being constructed. The continuation runs after the Loaded turn.
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            var imagePath = await _repositoryImageService.ResolveAsync(
                item.Path,
                cancellationToken);

            if (_disposed || cancellationToken.IsCancellationRequested) return;
            item.NeedsImageRefresh = false;
            item.SetRepositoryImagePath(imagePath);
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

    private Task RemoveAsync(RecentRepositoryItem item) => _settings.RemoveRecentRepositoryAsync(item.Path);
}
