namespace CSharpGit.Application.Tests;

public sealed class GitConsoleUiContractTests
{
    [Fact]
    public void GitConsoleKeepsDiagnosticAndWorkflowContractsVisible()
    {
        var root = FindRepositoryRoot();
        var consoleXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "GitConsoleView.xaml"));
        var typographyXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Typography.xaml"));
        var consoleCode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "GitConsoleView.xaml.cs"));
        var integration = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.GitConsole.cs"));
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));
        var settingsViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "SettingsViewModel.cs"));
        var operationBanner = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "OperationBanner.xaml"));

        Assert.Contains("Git Console", consoleXaml);
        Assert.Contains("CommandList", consoleXaml);
        Assert.Contains("ItemContainerStyle=\"{StaticResource DenseListItemStyle}\"", consoleXaml);
        Assert.Contains("StandardOutputText", consoleXaml);
        Assert.Contains("StandardErrorText", consoleXaml);
        Assert.Contains("TechnicalTextStyle", consoleXaml);
        Assert.Contains("TechnicalMultilineTextBoxStyle", consoleXaml);
        Assert.DoesNotContain("FontFamily=\"Consolas\"", consoleXaml);
        Assert.Contains("<Setter Property=\"FontFamily\" Value=\"Consolas\" />", typographyXaml);
        Assert.Contains("Copy command", consoleXaml);
        Assert.Contains("Copy all", consoleXaml);
        Assert.Contains("No Git commands recorded in this session.", consoleXaml);
        Assert.Contains("User commands", consoleCode);
        Assert.Contains("All commands", consoleCode);
        Assert.DoesNotContain("Run again", consoleXaml, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("InitializeGitConsole", integration);
        Assert.Contains("GitConsoleAutoOpenMode.Always", integration);
        Assert.Contains("GitConsoleAutoOpenMode.OnErrors", integration);
        Assert.Contains("GitCommandKind.User", integration);
        Assert.Contains("ToggleGitConsoleFromStatus", integration);
        Assert.Contains("GitConsoleShortcutKey = (VirtualKey)0xC0", integration);
        Assert.Contains("ToolTipService.SetToolTip", integration);

        Assert.Contains("Git Console", settingsXaml);
        Assert.Contains("GitConsoleAutoOpenComboBox", settingsXaml);
        Assert.Contains("latest 100 Git commands", settingsXaml);
        Assert.All(new[] { "On errors", "Always", "Never" }, label => Assert.Contains(label, settingsViewModel));

        Assert.Contains("Continue", operationBanner);
        Assert.Contains("Abort", operationBanner);
        Assert.Contains("Skip", operationBanner);
    }

    [Fact]
    public void GitConsoleSelectionUsesSingleDeferredScrollPath()
    {
        var root = FindRepositoryRoot();
        var consoleXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "GitConsoleView.xaml"));
        var consoleCode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "GitConsoleView.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var integration = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.GitConsole.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("SelectionMode=\"Single\"", consoleXaml);
        Assert.Contains("ScheduleScrollToSelectedActivity", consoleCode);
        Assert.Contains("DispatcherQueue.TryEnqueue", consoleCode);
        Assert.Contains("requestVersion != _scrollRequestVersion", consoleCode);
        Assert.Contains("SelectedActivityId != expectedActivityId", consoleCode);
        Assert.DoesNotContain(
            "CommandList.SelectedItem = item;\n        CommandList.ScrollIntoView(item);",
            consoleCode);
        Assert.DoesNotContain(
            "RebuildGitConsole(preferredSelection);\n        if (preferredSelection is { } id)\n            _gitConsoleView.SelectActivity(id);",
            integration);
        Assert.Contains("_gitConsoleView.SelectActivity(alreadyOpenId);", integration);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
