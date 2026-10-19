namespace CSharpGit.Application.Tests;

public sealed class UnoXamlCompatibilityContractTests
{
    [Fact]
    public void DenseListItemStylePreservesUnoSelectionVisualStates()
    {
        var workspace = ReadStyle("Workspace.xaml");
        var denseStyle = Slice(workspace,
            "<Style x:Key=\"DenseListItemStyle\"",
            "</Style>");

        Assert.Contains("BasedOn=\"{StaticResource ListViewItemExpanded}\"", denseStyle);
        Assert.DoesNotContain("<Setter Property=\"Template\">", denseStyle);
        Assert.DoesNotContain("<primitives:ListViewItemPresenter", workspace);

        foreach (var property in new[]
        {
            "SelectionCheckMarkVisualEnabled",
            "CheckBrush",
            "CheckBoxBrush",
            "FocusBorderBrush",
            "FocusSecondaryBorderBrush",
            "PointerOverBackground",
            "PointerOverForeground",
            "SelectedBackground",
            "SelectedForeground",
            "SelectedPointerOverBackground",
            "PressedBackground",
            "SelectedPressedBackground",
            "DisabledOpacity",
            "ContentMargin"
        })
        {
            Assert.DoesNotContain($"{property}=", workspace);
        }
    }

    [Fact]
    public void CompactTextBoxesKeepSemanticVerticalContentAlignment()
    {
        var controls = ReadStyle("Controls.xaml");
        var singleLine = Slice(controls,
            "<Style x:Key=\"CompactTextBoxStyle\"",
            "</Style>");
        var multiline = Slice(controls,
            "<Style x:Key=\"CompactMultilineTextBoxStyle\"",
            "</Style>");

        Assert.Contains("<Setter Property=\"VerticalContentAlignment\" Value=\"Center\" />", singleLine);
        Assert.Contains("BasedOn=\"{StaticResource CompactTextBoxStyle}\"", multiline);
        Assert.Contains("<Setter Property=\"VerticalContentAlignment\" Value=\"Top\" />", multiline);
    }

    [Fact]
    public void HistoryTextColumnsKeepSemanticGap()
    {
        var workspace = ReadStyle("Workspace.xaml");
        var historyReferences = ReadStyle("HistoryReferences.xaml");

        Assert.Contains("<Thickness x:Key=\"Margin.HistoryColumnGap\">4,0,0,0</Thickness>", workspace);

        var productionTemplateStart = historyReferences.IndexOf(
            "<DataTemplate x:Key=\"HistoryItemTemplate\"",
            StringComparison.Ordinal);
        var diagnosticTemplatesStart = historyReferences.IndexOf(
            "<DataTemplate x:Key=\"SimplifiedHistoryItemTemplate\"",
            StringComparison.Ordinal);
        Assert.True(productionTemplateStart >= 0);
        Assert.True(diagnosticTemplatesStart > productionTemplateStart);
        var productionTemplate = historyReferences[productionTemplateStart..diagnosticTemplatesStart];

        Assert.Equal(3, CountOccurrences(productionTemplate, "Margin=\"{StaticResource Margin.HistoryColumnGap}\""));
    }

    [Fact]
    public void GitCommandFeedbackUsesOpaqueThemeSurfaceInsideModalOverlay()
    {
        var workspace = ReadStyle("Workspace.xaml");
        var feedbackStyle = Slice(workspace,
            "<Style x:Key=\"FeedbackSurfaceStyle\"",
            "</Style>");
        var feedbackView = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "CSharpGit.Presentation", "Controls", "GitCommandFeedbackView.xaml"));

        Assert.Contains("SolidBackgroundFillColorTertiaryBrush", feedbackStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("CardBackgroundFillColorDefaultBrush", feedbackStyle, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource FeedbackSurfaceStyle}\"", feedbackView, StringComparison.Ordinal);
    }

    [Fact]
    public void GitCommandSuccessUsesSemanticThemeBrushAndRestoresNeutralStatusStyle()
    {
        var typography = ReadStyle("Typography.xaml");
        var success = Slice(typography, "<Style x:Key=\"SuccessStatusTextStyle\"", "</Style>");
        var presentation = Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation");
        var view = File.ReadAllText(Path.Combine(presentation, "Controls", "GitCommandFeedbackView.xaml"));
        var code = File.ReadAllText(Path.Combine(presentation, "Controls", "GitCommandFeedbackView.xaml.cs"));

        Assert.Contains("BasedOn=\"{StaticResource BodyTextStyle}\"", success, StringComparison.Ordinal);
        Assert.Contains("Property=\"Foreground\" Value=\"{ThemeResource SystemFillColorSuccessBrush}\"", success, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusText\"", view, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", view, StringComparison.Ordinal);
        Assert.Contains("var changedStatus = _current?.Status != state.Status;", code, StringComparison.Ordinal);
        Assert.Contains("state.Status == GitCommandStatus.Succeeded ? \"SuccessStatusTextStyle\" : \"BodyTextStyle\"", code, StringComparison.Ordinal);
        Assert.Contains("StatusText.Text = state.StatusText;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWorkspaceUsesSemanticActionStylesAndAFlatTonalToolbarSurface()
    {
        var controls = ReadStyle("Controls.xaml");
        var workspace = ReadStyle("Workspace.xaml");
        var mainPage = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "CSharpGit.Presentation", "MainPage.xaml"));

        var primary = Slice(controls, "<Style x:Key=\"PrimaryActionButtonStyle\"", "</Style>");
        var secondary = Slice(controls, "<Style x:Key=\"SecondaryActionButtonStyle\"", "</Style>");
        var toolbar = Slice(workspace, "<Style x:Key=\"ToolbarSurfaceStyle\"", "</Style>");

        Assert.Contains("BasedOn=\"{StaticResource CompactButtonStyle}\"", primary);
        Assert.Contains("AccentFillColorDefaultBrush", primary);
        Assert.Contains("BasedOn=\"{StaticResource CompactButtonStyle}\"", secondary);
        Assert.Contains("ControlFillColorDefaultBrush", secondary);
        Assert.Contains("CardBackgroundFillColorDefaultBrush", toolbar);
        Assert.DoesNotContain("Shadow", toolbar);
        Assert.Contains("x:Name=\"CommitNavigationButton\"", mainPage);
        Assert.Contains("Style=\"{StaticResource PrimaryActionButtonStyle}\"", mainPage);
        Assert.Contains("Style=\"{StaticResource SecondaryActionButtonStyle}\"", mainPage);
    }

    private static string ReadStyle(string fileName)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "Styles",
            fileName));
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker: {startMarker}");
        var end = value.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= start, $"Missing marker: {endMarker}");
        return value[start..(end + endMarker.Length)];
    }

    private static int CountOccurrences(string value, string marker)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
