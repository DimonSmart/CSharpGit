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
        Assert.Contains("TabFocusNavigation=\"Cycle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FloatingGitCommandFeedbackView\"", xaml, StringComparison.Ordinal);

        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var feedback = File.ReadAllText(Path.Combine(presentation, "Dialogs", "GitCommandFeedbackController.cs"));
        Assert.DoesNotContain("RootLayout.Children.Insert(", feedback, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(presentation, "MainPage.GitCommandFeedback.cs")));
        Assert.Contains("_floatingFeedbackView.Focus(FocusState.Programmatic)", feedback, StringComparison.Ordinal);
        Assert.Contains("_modalHost.Visibility = Visibility.Visible", feedback, StringComparison.Ordinal);
        Assert.Contains("_modalHost.Visibility = Visibility.Collapsed", feedback, StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml.cs"));
        var console = File.ReadAllText(Path.Combine(presentation, "Views", "GitConsoleHostView.xaml.cs"));
        Assert.Contains("IsShuttingDown || GitCommandFeedbackModalHost.Visibility == Visibility.Visible", page, StringComparison.Ordinal);
        Assert.Contains("_modalVisible?.Invoke() == true", console, StringComparison.Ordinal);
    }

    [Fact]
    public void CountdownIsInsideCloseButtonAndUsesCircularArc()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var xaml = File.ReadAllText(Path.Combine(presentation, "Controls", "GitCommandFeedbackView.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(presentation, "Controls", "GitCommandFeedbackView.xaml.cs"));

        var closeButton = xaml.IndexOf("x:Name=\"DismissButton\"", StringComparison.Ordinal);
        var countdown = xaml.IndexOf("x:Name=\"AutoCloseIndicator\"", StringComparison.Ordinal);
        var buttonEnd = xaml.IndexOf("</Button>", closeButton, StringComparison.Ordinal);
        Assert.True(closeButton >= 0 && countdown > closeButton && countdown < buttonEnd,
            "The countdown must be inside the existing Close button.");
        Assert.Contains("x:Name=\"DismissButtonLabel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AutoCloseCountdownArc\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AutoCloseTimeText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"AutoCloseProgress\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UpdateCountdownArc(remaining.TotalSeconds / total.TotalSeconds)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("AutoCloseTimeText.Text = $\"{seconds}s\";", codeBehind, StringComparison.Ordinal);
        Assert.Contains("AutoCloseIndicator.Visibility = Visibility.Collapsed", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DismissButtonLabel.Text = state.Status == GitCommandStatus.Running", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("DismissButton.Content =", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void CircularCountdownCoversBothHalfArcs()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation",
            "Controls", "GitCommandFeedbackView.xaml.cs"));

        Assert.Contains("Math.Clamp(remainingFraction, 0, 1)", code, StringComparison.Ordinal);
        Assert.Contains("GetCountdownPoint(Math.Min(fraction, 0.5))", code, StringComparison.Ordinal);
        Assert.Contains("if (fraction > 0.5)", code, StringComparison.Ordinal);
        Assert.Contains("_countdownFigure.Segments.Add(_countdownSecondArc)", code, StringComparison.Ordinal);
        Assert.Contains("_countdownFigure.Segments.RemoveAt(1)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapeCancelsAutoCloseWithoutClosingFeedbackDialog()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var xaml = File.ReadAllText(Path.Combine(presentation, "Controls", "GitCommandFeedbackView.xaml"));
        var view = File.ReadAllText(Path.Combine(presentation, "Controls", "GitCommandFeedbackView.xaml.cs"));
        var page = File.ReadAllText(Path.Combine(presentation, "Dialogs", "GitCommandFeedbackController.cs"));

        Assert.Contains("Press Esc to cancel auto-close", xaml, StringComparison.Ordinal);
        Assert.Contains("AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Feedback_KeyDown), handledEventsToo: true)", view, StringComparison.Ordinal);
        Assert.Contains("e.Key != VirtualKey.Escape", view, StringComparison.Ordinal);
        Assert.Contains("AutoCloseRemaining: not null", view, StringComparison.Ordinal);
        Assert.Contains("AutoCloseCancellationRequested?.Invoke(state.ActivityId)", view, StringComparison.Ordinal);
        Assert.Contains("_commandFeedback?.CancelAutoClose(id)", page, StringComparison.Ordinal);
        Assert.Contains("RenderGitCommandFeedback();", page, StringComparison.Ordinal);
        Assert.Contains("AutoCloseIndicator.Visibility = Visibility.Collapsed", view, StringComparison.Ordinal);
        var escapeHandler = view[view.IndexOf("private void Feedback_KeyDown", StringComparison.Ordinal)
            ..view.IndexOf("private void Dismiss_Click", StringComparison.Ordinal)];
        Assert.DoesNotContain("DismissRequested?.Invoke", escapeHandler, StringComparison.Ordinal);
    }

    [Fact]
    public void FeedbackAndConsoleUseSharedFollowTailPresentation()
    {
        var root = FindRepositoryRoot();
        var controls = Path.Combine(root, "src", "CSharpGit.Presentation", "Controls");
        var feedbackXaml = File.ReadAllText(Path.Combine(controls, "GitCommandFeedbackView.xaml"));
        var feedback = File.ReadAllText(Path.Combine(controls, "GitCommandFeedbackView.xaml.cs"));
        var console = File.ReadAllText(Path.Combine(controls, "GitConsoleView.xaml.cs"));

        Assert.Contains("TextWrapping=\"NoWrap\"", feedbackXaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"{StaticResource Font.Caption}\"", feedbackXaml, StringComparison.Ordinal);
        Assert.Contains("new GitOutputTextBoxPresenter(OutputText)", feedback, StringComparison.Ordinal);
        Assert.Contains("_outputPresenter.Update(state.PreviewText)", feedback, StringComparison.Ordinal);
        Assert.Contains("new GitOutputTextBoxPresenter(StandardOutputText)", console, StringComparison.Ordinal);
        Assert.Contains("new GitOutputTextBoxPresenter(StandardErrorText)", console, StringComparison.Ordinal);
        Assert.Contains("presenter.ApplyDelta(display, delta)", console, StringComparison.Ordinal);
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
