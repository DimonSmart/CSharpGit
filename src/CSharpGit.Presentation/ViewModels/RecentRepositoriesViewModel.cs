using System.Collections.ObjectModel;
using System.Windows.Input;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.ViewModels;

public sealed class RecentRepositoryItem
{
    internal RecentRepositoryItem(
        RecentRepositorySettings settings,
        Func<RecentRepositoryItem, Task> openAsync,
        Func<RecentRepositoryItem, Task> removeAsync)
    {
        Path = settings.Path;
        DisplayName = settings.DisplayName;
        LastOpenedUtc = settings.LastOpenedUtc;
        LastBranchName = settings.LastBranchName;
        IsAvailable = Directory.Exists(Path);
        TileOpacity = IsAvailable ? 1d : 0.5d;

        var localOpened = LastOpenedUtc.ToLocalTime();
        var openedText = $"Last opened {localOpened:g}";
        Metadata = string.IsNullOrWhiteSpace(LastBranchName)
            ? openedText
            : $"{LastBranchName}  ·  {openedText}";
        if (!IsAvailable) Metadata = $"Folder not found  ·  {Metadata}";

        OpenCommand = new AsyncCommand(() => openAsync(this), () => true);
        RemoveCommand = new AsyncCommand(() => removeAsync(this), () => true);
    }

    public string Path { get; }
    public string DisplayName { get; }
    public DateTimeOffset LastOpenedUtc { get; }
    public string? LastBranchName { get; }
    public bool IsAvailable { get; }
    public double TileOpacity { get; }
    public string Metadata { get; }
    public ICommand OpenCommand { get; }
    public ICommand RemoveCommand { get; }
}

public sealed class RecentRepositoriesViewModel
{
    private readonly IAppSettingsService _settings;
    private readonly Func<RecentRepositoryItem, Task> _openRecentAsync;

    internal RecentRepositoriesViewModel(
        IAppSettingsService settings,
        Func<RecentRepositoryItem, Task> openRecentAsync,
        Func<Task> openRepositoryAsync)
    {
        _settings = settings;
        _openRecentAsync = openRecentAsync;
        OpenRepositoryCommand = new AsyncCommand(openRepositoryAsync, () => true);
        _settings.Changed += Settings_Changed;
        Reload();
    }

    public ObservableCollection<RecentRepositoryItem> RecentRepositories { get; } = [];

    public ICommand OpenRepositoryCommand { get; }

    private void Settings_Changed(object? sender, EventArgs e) => Reload();

    private void Reload()
    {
        RecentRepositories.Clear();
        foreach (var settings in _settings.RecentRepositories)
            RecentRepositories.Add(new RecentRepositoryItem(settings, _openRecentAsync, RemoveAsync));
    }

    private Task RemoveAsync(RecentRepositoryItem item) => _settings.RemoveRecentRepositoryAsync(item.Path);
}
