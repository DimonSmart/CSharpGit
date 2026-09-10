using System.Text.RegularExpressions;

namespace CSharpGit.Application.Tests;

public sealed class DesignSystemContractTests
{
    [Fact]
    public void DesignResourcesAreCentralizedAndLoadedInDependencyOrder()
    {
        var root = FindRepositoryRoot();
        var app = Read(root, "src", "CSharpGit.Presentation", "App.xaml");
        var tokens = Read(root, "src", "CSharpGit.Presentation", "Styles", "DesignTokens.xaml");
        var typography = Read(root, "src", "CSharpGit.Presentation", "Styles", "Typography.xaml");
        var controls = Read(root, "src", "CSharpGit.Presentation", "Styles", "Controls.xaml");
        var workspace = Read(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml");

        var tokenIndex = app.IndexOf("Styles/DesignTokens.xaml", StringComparison.Ordinal);
        var typographyIndex = app.IndexOf("Styles/Typography.xaml", StringComparison.Ordinal);
        var controlsIndex = app.IndexOf("Styles/Controls.xaml", StringComparison.Ordinal);
        var workspaceIndex = app.IndexOf("Styles/Workspace.xaml", StringComparison.Ordinal);

        Assert.True(tokenIndex >= 0 && tokenIndex < typographyIndex && typographyIndex < controlsIndex && controlsIndex < workspaceIndex);

        foreach (var key in new[]
        {
            "Spacing.XS", "Spacing.S", "Spacing.M", "Spacing.L", "Spacing.XL", "Spacing.XXL",
            "Font.Caption", "Font.Body", "Font.Heading", "Font.Title",
            "Height.DataRow", "Height.Control", "Height.Header", "Height.ColumnHeader", "Height.TabHeader",
            "Height.Toolbar", "Height.StatusBar", "Height.DiffRow", "Height.CommitEditor",
            "Icon.Small", "Icon.Normal"
        })
            Assert.Contains($"x:Key=\"{key}\"", tokens);

        foreach (var style in new[]
        {
            "BodyTextStyle", "BodyStrongTextStyle", "SecondaryTextStyle", "CaptionTextStyle",
            "PaneHeaderTextStyle", "SectionHeaderTextStyle", "ToolbarProductTextStyle", "TitleTextStyle", "DiffTextStyle"
        })
            Assert.Contains($"x:Key=\"{style}\"", typography);

        Assert.Contains("BasedOn=\"{StaticResource BodyTextStyle}\"", typography);
        Assert.Contains("BasedOn=\"{StaticResource CompactButtonStyle}\"", controls);
        Assert.Contains("BasedOn=\"{StaticResource DenseListItemStyle}\"", workspace);
        Assert.Contains("BasedOn=\"{StaticResource DenseTreeItemStyle}\"", workspace);
    }

    [Fact]
    public void WorkspaceUsesSharedSemanticDensityStylesAndMetrics()
    {
        var root = FindRepositoryRoot();
        var main = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml");
        var tokens = Read(root, "src", "CSharpGit.Presentation", "Styles", "DesignTokens.xaml");
        var workspace = Read(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml");

        Assert.Contains("<x:Double x:Key=\"Height.DataRow\">24</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.Header\">26</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.ColumnHeader\">24</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.TabHeader\">28</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.Toolbar\">34</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.StatusBar\">22</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.DiffRow\">20</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.CommitEditor\">60</x:Double>", tokens);

        Assert.Equal(2, Count(main, "ItemContainerStyle=\"{StaticResource WorkingTreeRowStyle}\""));
        Assert.Contains("ItemContainerStyle=\"{StaticResource DenseTreeItemStyle}\"", main);
        Assert.Contains("ItemTemplate=\"{StaticResource RepositoryTreeItemTemplate}\"", main);
        Assert.Contains("ItemContainerStyle=\"{StaticResource HistoryRowStyle}\"", main);
        Assert.Contains("ItemTemplate=\"{StaticResource HistoryItemTemplate}\"", main);
        Assert.Contains("ItemContainerStyle=\"{StaticResource ChangedFileRowStyle}\"", main);
        Assert.Contains("ItemTemplate=\"{StaticResource ChangedFileTreeItemTemplate}\"", main);
        Assert.True(Count(main, "ItemContainerStyle=\"{StaticResource DiffRowStyle}\"") >= 2);
        Assert.True(Count(main, "ItemTemplate=\"{StaticResource DiffItemTemplate}\"") >= 2);
        Assert.Contains("Style=\"{StaticResource ToolbarSurfaceStyle}\"", main);
        Assert.Contains("Style=\"{StaticResource StatusBarSurfaceStyle}\"", main);
        Assert.Contains("Style=\"{StaticResource DenseColumnHeaderSurfaceStyle}\"", main);
        Assert.Contains("Style=\"{StaticResource PaneHeaderTextStyle}\"", main);

        foreach (var name in new[] { "MainToolbar", "HistoryFilterToolbar", "HistoryColumnHeader", "ChangedFilesHeader", "StatusBar" })
            Assert.Contains($"x:Name=\"{name}\"", main);

        Assert.Contains("x:Key=\"DenseListItemStyle\"", workspace);
        Assert.Contains("x:Key=\"DenseTreeItemStyle\"", workspace);
        Assert.Contains("x:Key=\"WorkingTreeRowStyle\"", workspace);
        Assert.Contains("x:Key=\"HistoryRowStyle\"", workspace);
        Assert.Contains("x:Key=\"ChangedFileRowStyle\"", workspace);
        Assert.Contains("x:Key=\"DiffRowStyle\"", workspace);
        Assert.Contains("x:Key=\"DenseColumnHeaderSurfaceStyle\"", workspace);
        Assert.Contains("x:Key=\"CompactPivotHeaderItemStyle\"", workspace);
        Assert.Contains("primitives:ListViewItemPresenter", workspace);
        Assert.DoesNotContain("Height=\"24\"", workspace);
        Assert.DoesNotContain("Height=\"20\"", workspace);

        Assert.DoesNotContain("Staged and unstaged changes are shown independently.", main);
        Assert.Contains("ToolTipService.ToolTip=\"Stage selected\"", main);
        Assert.Contains("AutomationProperties.Name=\"Stage selected\"", main);
        Assert.Contains("ToolTipService.ToolTip=\"Discard selected changes\"", main);
        Assert.Contains("AutomationProperties.Name=\"Discard selected changes\"", main);
        Assert.Contains("AutomationProperties.Name=\"Commit message\"", main);
    }

    [Fact]
    public void CommitGraphLayoutDoesNotOwnHistoryRowHeight()
    {
        var root = FindRepositoryRoot();
        var metrics = Read(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphMetrics.cs");
        var control = Read(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphControl.cs");
        var builder = Read(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphGeometryBuilder.cs");

        Assert.DoesNotContain("DefaultRowHeight", metrics);
        Assert.DoesNotContain("DefaultRowHeight", control);
        Assert.DoesNotContain("DefaultRowHeight", builder);
        Assert.DoesNotContain("Height.DataRow", control);
        Assert.DoesNotContain("desiredHeight", control);
    }

    [Fact]
    public void RuntimeCompactStylingAndParallelCompactResourcesAreGone()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var main = File.ReadAllText(Path.Combine(presentation, "MainPage.xaml"));
        var workingTree = File.ReadAllText(Path.Combine(presentation, "MainPage.WorkingTreeDiff.cs"));
        var changes = File.ReadAllText(Path.Combine(presentation, "MainPage.Changes.cs"));
        var mainPageSources = Directory.EnumerateFiles(presentation, "MainPage*.cs")
            .Select(File.ReadAllText)
            .ToArray();

        Assert.False(File.Exists(Path.Combine(presentation, "MainPage.CompactLayout.cs")));
        Assert.False(File.Exists(Path.Combine(presentation, "CompactWorkspaceResources.xaml")));

        foreach (var source in mainPageSources)
        {
            Assert.DoesNotContain("_compactLayoutApplied", source);
            Assert.DoesNotContain("ApplyCompactLayoutWhenLoaded", source);
            Assert.DoesNotContain("ApplyCompactWorkspaceLayout", source);
            Assert.DoesNotContain("CompactResource<", source);
        }

        Assert.DoesNotContain(".ItemContainerStyle =", workingTree);
        Assert.DoesNotContain(".ItemTemplate =", workingTree);
        Assert.DoesNotContain(".MinHeight =", changes);
        Assert.DoesNotContain("UpdateCommitDiffHeaderRow", changes);

        Assert.DoesNotContain("CompactHistoryItemTemplate", main);
        Assert.DoesNotContain("CompactRepositoryTreeItemTemplate", main);
        Assert.DoesNotContain("CompactChangedFileTreeItemTemplate", main);
        Assert.DoesNotContain("CompactDiffItemTemplate", main);
    }

    [Fact]
    public void WorkspaceStandardMetricsDoNotRegressToLiteralMagicNumbers()
    {
        var root = FindRepositoryRoot();
        var main = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml");
        var start = main.IndexOf("<Grid x:Name=\"RepositoryWorkspace\"", StringComparison.Ordinal);
        var end = main.IndexOf("<InfoBar IsOpen=\"{Binding HasError}\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var workspaceSurface = main[start..end];

        Assert.DoesNotMatch(new Regex("FontSize=\"(?:9|10|11|12|13|14|15|16|17|18)\""), workspaceSurface);
        Assert.DoesNotMatch(new Regex("(?:MinHeight|Height)=\"(?:20|22|24|26|28|30|32|34|36|40|44|46|54)\""), workspaceSurface);
        Assert.DoesNotContain("Padding=\"6,3\"", workspaceSurface);
        Assert.DoesNotContain("Padding=\"8,5\"", workspaceSurface);
    }

    [Fact]
    public void SecondaryScreensUseTheSharedTypographyAndSpacingScale()
    {
        var root = FindRepositoryRoot();
        var settings = Read(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml");
        var recent = Read(root, "src", "CSharpGit.Presentation", "RecentRepositoriesView.xaml");

        foreach (var xaml in new[] { settings, recent })
        {
            Assert.Contains("TitleTextStyle", xaml);
            Assert.Contains("BodyTextStyle", xaml);
            Assert.Contains("SecondaryTextStyle", xaml);
            Assert.Contains("Spacing.", xaml);
        }

        Assert.Contains("SectionHeaderTextStyle", settings);
        Assert.Contains("CompactComboBoxStyle", settings);
        Assert.DoesNotContain("FontSize=\"18\"", settings);
        Assert.DoesNotContain("FontSize=\"15\"", recent);
        Assert.DoesNotContain("FontSize=\"12\"", recent);
    }

    private static string Read(string root, params string[] parts)
    {
        var path = root;
        foreach (var part in parts) path = Path.Combine(path, part);
        return File.ReadAllText(path);
    }

    private static int Count(string value, string fragment) =>
        (value.Length - value.Replace(fragment, string.Empty, StringComparison.Ordinal).Length) / fragment.Length;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
