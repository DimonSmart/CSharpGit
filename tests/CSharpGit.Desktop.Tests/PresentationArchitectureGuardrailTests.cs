using System.Text.RegularExpressions;

namespace CSharpGit.Desktop.Tests;

public sealed class PresentationArchitectureGuardrailTests
{
    [Fact]
    public void ProductionSourceDoesNotReintroduceGlobalApplicationServiceAccess()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");
        var forbidden = new[]
        {
            "AppSettingsContext.Current",
            "RepositoryImageServices.Current",
            "GitCommandActivitySession.Current",
            "GitCommandExecutor.Default"
        };

        foreach (var file in ProductionCsFiles(sourceRoot))
        {
            var source = File.ReadAllText(file);
            foreach (var pattern in forbidden)
                Assert.DoesNotContain(pattern, source, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(Path.Combine(root, "src", "CSharpGit.Presentation", "AppSettingsContext.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src", "CSharpGit.Presentation", "RepositoryImageServices.cs")));
    }

    [Fact]
    public void PresentationCreatesApplicationWideServicesOnlyInCompositionRoot()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var appPath = Path.Combine(presentation, "App.xaml.cs");
        var app = File.ReadAllText(appPath);

        foreach (var file in ProductionCsFiles(presentation).Where(path => !Path.GetFullPath(path).Equals(Path.GetFullPath(appPath), StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("new JsonAppSettingsService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new RepositoryImageService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new GitCommandActivityHistory", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetRequiredService<", source, StringComparison.Ordinal);
        }

        Assert.Contains("new JsonAppSettingsService()", app, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IRepositoryImageService, RepositoryImageService>()", app, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<GitCommandActivityHistory>()", app, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPageHasOnePublicConstructionPathAndNoExternalInitializationProtocol()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));
        var mainPageSources = Directory
            .GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join(Environment.NewLine, mainPageSources);

        Assert.Single(Regex.Matches(combined, @"\bpublic\s+MainPage\s*\(").Cast<Match>());
        Assert.DoesNotContain("mainPage.Initialize", app, StringComparison.Ordinal);
        Assert.DoesNotContain("MainPageServices", combined, StringComparison.Ordinal);
        Assert.Contains("SettingsWindowController", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPageRepositoryCollectionSubscriptionsAreNamedAndSymmetricallyDetached()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var mainPage = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));
        var lifecycle = File.ReadAllText(Path.Combine(presentation, "MainPage.Lifecycle.cs"));

        var subscriptions = new[]
        {
            ("Changes", "RepositoryPresentationChanges_CollectionChanged"),
            ("LocalBranches", "RepositoryPresentationLocalBranches_CollectionChanged"),
            ("RemoteBranches", "RepositoryPresentationRemoteBranches_CollectionChanged"),
            ("Remotes", "RepositoryPresentationRemotes_CollectionChanged"),
            ("Tags", "RepositoryPresentationTags_CollectionChanged"),
            ("Stashes", "RepositoryPresentationStashes_CollectionChanged")
        };

        foreach (var (collection, handler) in subscriptions)
        {
            Assert.Contains($"_viewModel.{collection}.CollectionChanged += {handler};", mainPage, StringComparison.Ordinal);
            Assert.Contains($"_viewModel.{collection}.CollectionChanged -= {handler};", lifecycle, StringComparison.Ordinal);
            Assert.DoesNotContain($"_viewModel.{collection}.CollectionChanged += (_, _) =>", mainPage, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> ProductionCsFiles(string root) =>
        Directory
            .GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
