namespace CSharpGit.Application.Tests;

public sealed class RepositoryIdentityUiContractTests
{
    [Fact]
    public void IdentityIsDedicatedGitBackedSettingsPage()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml.cs"));
        var controller = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsWindowController.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "RepositoryIdentitySettingsViewModel.cs"));
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var mainSettings = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Settings.cs"));
        var appSettings = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Application", "Abstractions", "IAppSettingsService.cs"));

        Assert.Contains("x:Name=\"IdentitySettingsPanel\"", xaml);
        Assert.Contains("Text=\"Identity\"", xaml);
        Assert.Contains("Text=\"Name\"", xaml);
        Assert.Contains("Text=\"Email\"", xaml);
        Assert.Contains("Save for this repository", xaml);
        Assert.Equal(1, CountOccurrences(xaml, "Click=\"IdentityNameRemove_Click\""));
        Assert.Equal(1, CountOccurrences(xaml, "Click=\"IdentityEmailRemove_Click\""));
        Assert.Contains("Open a repository to configure its Git identity.", xaml);
        Assert.Contains("Base Git identity for commits in this repository.", xaml);
        Assert.Contains("Effective", xaml);
        Assert.Contains("Source", xaml);
        Assert.Contains("Origin", xaml);

        Assert.Contains("SettingsSection.Identity", page);
        Assert.Contains("NavigationOrder", page);
        Assert.DoesNotContain("Math.Clamp(SettingsNavigation.SelectedIndex", page);
        Assert.Contains("RepositoryChanged()", page);
        Assert.Contains("ActivateSectionAsync(section, force: true)", page);
        Assert.Contains("ShowSettingsSuccess(\"Identity\", \"Repository identity saved.\")", page);

        Assert.Contains("IRepositoryIdentityService _repositoryIdentityService", controller);
        Assert.Contains("RepositoryIdentitySettingsViewModel(_repositoryIdentityService, repositoryAccessor)", controller);
        Assert.Contains("RepositoryChanged() => _page?.RepositoryChanged()", controller);

        Assert.Contains("Repository settings…", mainXaml);
        Assert.Contains("RepositorySettings_Click", mainXaml);
        Assert.Contains("OpenSettingsWindow(SettingsSection.Identity)", mainSettings);
        Assert.Contains("Func<Repository?> _repositoryAccessor", viewModel);
        Assert.DoesNotContain("IAppSettingsService", viewModel);
        Assert.DoesNotContain("userName", appSettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userEmail", appSettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("repositoryIdentity", appSettings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsNavigationOrderIsGeneralIdentityGitToolsDiagnostics()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml.cs"));

        var general = xaml.IndexOf("Text=\"General\"", StringComparison.Ordinal);
        var identity = xaml.IndexOf("Text=\"Identity\"", StringComparison.Ordinal);
        var gitTools = xaml.IndexOf("Text=\"Git Tools\"", StringComparison.Ordinal);
        var diagnostics = xaml.IndexOf("Text=\"Diagnostics\"", StringComparison.Ordinal);

        Assert.True(general >= 0 && general < identity && identity < gitTools && gitTools < diagnostics);
        Assert.Contains("SettingsSection.General", page);
        Assert.Contains("SettingsSection.Identity", page);
        Assert.Contains("SettingsSection.GitTools", page);
        Assert.Contains("SettingsSection.Diagnostics", page);
    }

    [Fact]
    public void IdentityDoesNotPersistToApplicationSettingsOrInjectCommitAuthorOverrides()
    {
        var root = FindRepositoryRoot();
        var identityService = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "RepositoryIdentityService.cs"));
        var workingTree = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitWorkingTreeService.cs"));
        var settingsJson = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "appsettings.json"));

        Assert.Contains("\"user.name\"", identityService);
        Assert.Contains("\"user.email\"", identityService);
        Assert.Contains("GitConfigScope.Repository", identityService);
        Assert.DoesNotContain("IAppSettingsService", identityService);
        Assert.DoesNotContain("GIT_AUTHOR_NAME", workingTree);
        Assert.DoesNotContain("GIT_AUTHOR_EMAIL", workingTree);
        Assert.DoesNotContain("GIT_COMMITTER_NAME", workingTree);
        Assert.DoesNotContain("GIT_COMMITTER_EMAIL", workingTree);
        Assert.DoesNotContain("userName", settingsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userEmail", settingsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("repositoryIdentity", settingsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitToolsAndIdentityShareGenericGitConfigLayer()
    {
        var root = FindRepositoryRoot();
        var gitTools = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitToolsService.cs"));
        var identity = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "RepositoryIdentityService.cs"));
        var config = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Git", "GitConfigService.cs"));

        Assert.Contains("GitConfigService _configService", gitTools);
        Assert.Contains("GitConfigService _config", identity);
        Assert.Contains("--show-scope", config);
        Assert.Contains("--show-origin", config);
        Assert.Contains("--no-includes", config);
        Assert.Contains("--replace-all", config);
        Assert.Contains("--unset-all", config);
        Assert.DoesNotContain("ParseEffectiveConfig", gitTools);
        Assert.DoesNotContain("ParseScopedConfig", gitTools);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
