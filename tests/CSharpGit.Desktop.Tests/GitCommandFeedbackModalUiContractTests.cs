namespace CSharpGit.Desktop.Tests;

public sealed class GitCommandFeedbackModalUiContractTests
{
    [Fact]
    public void ModalOverlayIsAboveWorkspaceAndCyclesKeyboardFocus()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var host = xaml.IndexOf("x:Name=\"GitCommandFeedbackModalHost\"", StringComparison.Ordinal);
        var notifications = xaml.LastIndexOf("<InfoBar ", StringComparison.Ordinal);
        Assert.True(host > notifications, "The modal host must appear after all other root children.");
        Assert.Contains("Background=\"#99000000\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TabFocusNavigation=\"Cycle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FloatingGitCommandFeedbackView\"", xaml, StringComparison.Ordinal);

        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));
        Assert.Contains("x:Name=\"SuccessfulGitCommandFeedbackDurationNumberBox\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("Minimum=\"0\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("Maximum=\"30\"", settingsXaml, StringComparison.Ordinal);

        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var feedback = File.ReadAllText(Path.Combine(presentation, "MainPage.GitCommandFeedback.cs"));
        Assert.DoesNotContain("RootLayout.Children.Insert(", feedback, StringComparison.Ordinal);
        Assert.Contains("_floatingFeedbackView.Focus(FocusState.Programmatic)", feedback, StringComparison.Ordinal);
        Assert.Contains("GitCommandFeedbackModalHost.Visibility = Visibility.Visible", feedback, StringComparison.Ordinal);
        Assert.Contains("GitCommandFeedbackModalHost.Visibility = Visibility.Collapsed", feedback, StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));
        var console = File.ReadAllText(Path.Combine(presentation, "MainPage.GitConsole.cs"));
        Assert.Contains("IsShuttingDown || GitCommandFeedbackModalHost.Visibility == Visibility.Visible", page, StringComparison.Ordinal);
        Assert.Contains("if (GitCommandFeedbackModalHost.Visibility == Visibility.Visible) return;", console, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
