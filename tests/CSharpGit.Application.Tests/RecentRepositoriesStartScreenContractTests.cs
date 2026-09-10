namespace CSharpGit.Application.Tests;

public sealed class RecentRepositoriesStartScreenContractTests
{
    [Fact]
    public void RecentRepositoryLauncherPreservesEmptyStateAndTracksRepositoryMetadata()
    {
        var root = FindRepositoryRoot();
        var settingsContract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IAppSettingsService.cs"));
        var imageContract = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IRepositoryImageService.cs"));
        var settingsPersistence = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Infrastructure", "JsonAppSettingsService.cs"));
        var imageService = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Infrastructure", "RepositoryImageService.cs"));
        var localImageProvider = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Infrastructure", "LocalRepositoryImageProvider.cs"));
        var githubImageProvider = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Infrastructure", "GitHubRepositoryImageProvider.cs"));
        var imageInternals = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Infrastructure", "RepositoryImageInternals.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "RepositoryImageServices.cs"));
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var recentView = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "RecentRepositoriesView.xaml"));
        var recentViewCodeBehind = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "RecentRepositoriesView.xaml.cs"));
        var recentViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RecentRepositoriesViewModel.cs"));
        var integration = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RecentRepositories.cs"));

        Assert.Contains("RecentRepositorySettings", settingsContract);
        Assert.Contains("LastOpenedUtc", settingsContract);
        Assert.Contains("LastBranchName", settingsContract);
        Assert.DoesNotContain("RepositoryImage", settingsContract);
        Assert.Contains("MaxRecentRepositories = 8", settingsPersistence);
        Assert.Contains("Take(MaxRecentRepositories)", settingsPersistence);
        Assert.Contains("PathComparer.Equals", settingsPersistence);

        Assert.Contains("IRepositoryImageService", imageContract);
        Assert.Contains("RepositoryImageCacheState", imageContract);
        Assert.DoesNotContain("BitmapImage", imageContract);
        Assert.Contains("LocalRepositoryImageProvider", imageService);
        Assert.Contains("FindLocalCandidate", localImageProvider);
        Assert.Contains("GitHubRepositoryImageProvider", imageService);
        Assert.Contains("og:image", githubImageProvider);
        Assert.Contains("SemaphoreSlim _remoteRequestGate = new(4, 4)", imageService);
        Assert.Contains("TimeSpan.FromDays(7)", imageService);
        Assert.Contains("TimeSpan.FromHours(24)", imageService);
        Assert.Contains("MaxCacheBytes = 50L * 1024 * 1024", imageService);
        Assert.Contains("MaxImageBytes = 5L * 1024 * 1024", imageService);
        Assert.Contains("\"repository-icon\"", imageInternals);
        Assert.Contains("\"logo\"", imageInternals);
        Assert.Contains("\"icon\"", imageInternals);
        
        Assert.Contains("RepositoryImageServices", composition);
        Assert.Contains("IRepositoryImageService", composition);

        Assert.Contains("Open a Git repository", mainPage);
        Assert.Contains("Select the folder of an existing repository or worktree.", mainPage);
        Assert.Contains("Recent repositories", recentView);
        Assert.Contains("Open repository", recentView);
        Assert.Contains("RemoveCommand", recentView);
        Assert.Contains("TileOpacity", recentView);
        Assert.Contains("VariableSizedWrapGrid", recentView);
        Assert.DoesNotContain("ItemsWrapGrid", recentView);
        Assert.Contains("Width=\"64\" Height=\"64\"", recentView);
        Assert.Contains("Source=\"{Binding RepositoryImage}\"", recentView);
        Assert.Contains("RepositoryGlyphOpacity", recentView);
        Assert.Contains("ImageFailed=\"RepositoryImage_ImageFailed\"", recentView);
        Assert.Contains("SetRepositoryImagePath(null)", recentViewCodeBehind);
        Assert.Contains("StartImageLoading()", recentViewCodeBehind);

        Assert.Contains("Folder not found", recentViewModel);
        Assert.Contains("INotifyPropertyChanged", recentViewModel);
        Assert.Contains("GetCachedState(item.Path)", recentViewModel);
        Assert.Contains("item.SetRepositoryImagePath(cached.ImagePath)", recentViewModel);
        Assert.Contains("await Task.Yield()", recentViewModel);
        Assert.Contains("ResolveAsync(", recentViewModel);
        Assert.Contains("item.SetRepositoryImagePath(imagePath)", recentViewModel);
        Assert.Contains("CancellationTokenSource", recentViewModel);

        Assert.Contains("Directory.Exists(item.Path)", integration);
        Assert.Contains("RecordRecentRepositoryAsync", integration);
        Assert.Contains("branch.IsCurrent", integration);
        Assert.DoesNotContain("RemoveRecentRepositoryAsync(item.Path)", integration);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
