using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CSharpGit.Application.Tests;

public sealed class DesignSystemContractTests
{
    [Fact]
    public void DesignResourcesAreCentralizedLoadedInDependencyOrderAndHaveSingleOwners()
    {
        var root = FindRepositoryRoot();
        var app = Read(root, "src", "CSharpGit.Presentation", "App.xaml");
        var tokens = Read(root, "src", "CSharpGit.Presentation", "Styles", "DesignTokens.xaml");
        var typography = Read(root, "src", "CSharpGit.Presentation", "Styles", "Typography.xaml");
        var controls = Read(root, "src", "CSharpGit.Presentation", "Styles", "Controls.xaml");
        var workspace = Read(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml");
        var repositoryTree = Read(root, "src", "CSharpGit.Presentation", "Styles", "RepositoryTree.xaml");
        var historyReferences = Read(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml");

        var sources = new[]
        {
            "Styles/DesignTokens.xaml",
            "Styles/Typography.xaml",
            "Styles/Controls.xaml",
            "Styles/Workspace.xaml",
            "Styles/RepositoryTree.xaml",
            "Styles/HistoryReferences.xaml"
        };

        var previousIndex = -1;
        foreach (var source in sources)
        {
            var index = app.IndexOf(source, StringComparison.Ordinal);
            Assert.True(index > previousIndex, $"{source} must be loaded after its dependencies.");
            previousIndex = index;
        }

        foreach (var key in new[]
        {
            "Spacing.XS", "Spacing.S", "Spacing.M", "Spacing.L", "Spacing.XL", "Spacing.XXL",
            "Font.Caption", "Font.Body", "Font.Heading", "Font.Title",
            "Height.DataRow", "Height.Control", "Height.Header", "Height.ColumnHeader", "Height.TabHeader",
            "Height.Toolbar", "Height.StatusBar", "Height.DiffRow", "Height.CommitEditor",
            "Icon.Small", "Icon.Normal", "Margin.FormSeparator"
        })
            Assert.Contains($"x:Key=\"{key}\"", tokens);

        foreach (var style in new[]
        {
            "BodyTextStyle", "BodyStrongTextStyle", "BodySubduedTextStyle",
            "SecondaryTextStyle", "CaptionTextStyle", "CaptionStrongTextStyle",
            "PaneHeaderTextStyle", "SectionHeaderTextStyle", "ToolbarProductTextStyle", "TitleTextStyle",
            "TechnicalTextStyle", "TechnicalSecondaryTextStyle", "DiffTextStyle"
        })
            Assert.Contains($"x:Key=\"{style}\"", typography);

        foreach (var style in new[]
        {
            "CompactButtonStyle", "IconButtonStyle", "CompactTextBoxStyle", "CompactMultilineTextBoxStyle",
            "TechnicalTextBoxStyle", "TechnicalMultilineTextBoxStyle",
            "CompactComboBoxStyle", "CompactCheckBoxStyle"
        })
            Assert.Contains($"x:Key=\"{style}\"", controls);

        Assert.Contains("BasedOn=\"{StaticResource TechnicalTextStyle}\"", typography);
        Assert.Contains("BasedOn=\"{StaticResource CompactButtonStyle}\"", controls);
        Assert.Contains("BasedOn=\"{StaticResource DenseListItemStyle}\"", workspace);
        Assert.Contains("BasedOn=\"{StaticResource DenseTreeItemStyle}\"", repositoryTree);

        var dictionaries = new Dictionary<string, string>
        {
            ["DesignTokens.xaml"] = tokens,
            ["Typography.xaml"] = typography,
            ["Controls.xaml"] = controls,
            ["Workspace.xaml"] = workspace,
            ["RepositoryTree.xaml"] = repositoryTree,
            ["HistoryReferences.xaml"] = historyReferences
        };

        var duplicateKeys = dictionaries
            .SelectMany(pair => ResourceKeys(pair.Value).Select(key => (key, owner: pair.Key)))
            .GroupBy(item => item.key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(item => item.owner))}")
            .ToArray();

        Assert.True(duplicateKeys.Length == 0,
            $"Merged application dictionaries contain competing resource keys: {string.Join("; ", duplicateKeys)}");

        Assert.Equal(1, Count(historyReferences, "x:Key=\"HistoryItemTemplate\""));
        Assert.DoesNotContain("x:Key=\"HistoryItemTemplate\"", workspace);

        foreach (var key in new[]
                 {
                     "DenseTreeItemStyle",
                     "RepositoryTreeItemStyle",
                     "RepositoryTreeItemTemplate",
                     "RepositoryFilesTreeItemTemplate",
                     "WorkingTreeTreeItemTemplate",
                     "ChangedFileTreeItemTemplate"
                 })
        {
            Assert.Equal(1, Count(repositoryTree, $"x:Key=\"{key}\""));
            Assert.DoesNotContain($"x:Key=\"{key}\"", workspace);
        }
    }

    [Fact]
    public void WorkspaceUsesSharedSemanticDensityStylesAndMetrics()
    {
        var root = FindRepositoryRoot();
        var main = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml");
        var tokens = Read(root, "src", "CSharpGit.Presentation", "Styles", "DesignTokens.xaml");
        var workspace = Read(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml");
        var repositoryTree = Read(root, "src", "CSharpGit.Presentation", "Styles", "RepositoryTree.xaml");
        var historyReferences = Read(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml");

        Assert.Contains("<x:Double x:Key=\"Height.DataRow\">24</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.Header\">26</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.ColumnHeader\">24</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.TabHeader\">28</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.Toolbar\">34</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.StatusBar\">22</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.DiffRow\">20</x:Double>", tokens);
        Assert.Contains("<x:Double x:Key=\"Height.CommitEditor\">60</x:Double>", tokens);

        Assert.True(Count(main, "ItemContainerStyle=\"{StaticResource DenseTreeItemStyle}\"") >= 4);
        Assert.Equal(2, Count(main, "ItemTemplate=\"{StaticResource WorkingTreeTreeItemTemplate}\""));
        Assert.Contains("ItemTemplate=\"{StaticResource RepositoryTreeItemTemplate}\"", main);
        Assert.Contains("ItemContainerStyle=\"{StaticResource HistoryRowStyle}\"", main);
        Assert.Contains("ItemTemplate=\"{StaticResource HistoryItemTemplate}\"", main);
        Assert.DoesNotContain("ChangedFileRowStyle", main);
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
        Assert.DoesNotContain("x:Key=\"WorkingTreeRowStyle\"", workspace);
        Assert.Contains("x:Key=\"HistoryRowStyle\"", workspace);
        Assert.DoesNotContain("x:Key=\"ChangedFileRowStyle\"", workspace);
        Assert.Contains("x:Key=\"DiffRowStyle\"", workspace);
        Assert.Contains("x:Key=\"DenseColumnHeaderSurfaceStyle\"", workspace);
        Assert.Contains("x:Key=\"CompactPivotHeaderItemStyle\"", workspace);
        Assert.Contains("x:Key=\"DenseTreeItemStyle\"", repositoryTree);
        Assert.Contains("x:Key=\"WorkingTreeTreeItemTemplate\"", repositoryTree);
        Assert.Contains("x:Key=\"ChangedFileTreeItemTemplate\"", repositoryTree);
        Assert.Contains("MinHeight=\"{StaticResource Height.DataRow}\"", historyReferences);
        Assert.DoesNotContain("Height=\"24\"", workspace);
        Assert.DoesNotContain("Height=\"20\"", workspace);

        Assert.DoesNotContain("Staged and unstaged changes are shown independently.", main);
        Assert.Contains("ToolTipService.ToolTip=\"Stage selected\"", main);
        Assert.Contains("AutomationProperties.Name=\"Stage selected\"", main);
        Assert.Contains("ToolTipService.ToolTip=\"Discard selected\"", main);
        Assert.Contains("AutomationProperties.Name=\"Discard selected\"", main);
        Assert.Contains("ToolTipService.ToolTip=\"Discard all…\"", main);
        Assert.Contains("AutomationProperties.Name=\"Discard all\"", main);
        Assert.Contains("AutomationProperties.Name=\"Commit message\"", main);
    }

    [Fact]
    public void CommitDetailsUseBodyTypographyAndSharedTechnicalRoles()
    {
        var root = FindRepositoryRoot();
        var main = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml");
        var details = Read(root, "src", "CSharpGit.Presentation", "Controls", "CommitDetailsView.xaml");

        var bodyMessage = new Regex(
            "Text=\"\\{Binding SelectedHistoryRow\\.Commit\\.Message\\}\"\\s+Style=\"\\{StaticResource BodyTextStyle\\}\"",
            RegexOptions.CultureInvariant);

        Assert.Contains("<controls:CommitDetailsView x:Name=\"CommitDetailsContent\" />", main);
        Assert.Matches(bodyMessage, details);
        Assert.DoesNotMatch(
            new Regex("SelectedHistoryRow\\.Commit\\.Message[\\s\\S]{0,120}SectionHeaderTextStyle", RegexOptions.CultureInvariant),
            details);

        Assert.Contains("Style=\"{StaticResource TechnicalTextStyle}\"", details);
        Assert.DoesNotContain("Style=\"{StaticResource DiffTextStyle}\"", details);
    }

    [Fact]
    public void GitOperationsAndSettingsGitToolsUseSharedCompactControlRoles()
    {
        var root = FindRepositoryRoot();
        var main = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml");
        var settings = Read(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml");

        var gitOperations = Slice(main,
            "<ContentDialog x:Key=\"GitOperationsDialog\"",
            "</ContentDialog>");
        Assert.Contains("BodyStrongTextStyle", gitOperations);
        Assert.True(Count(gitOperations, "CompactButtonStyle") >= 9);
        Assert.True(Count(gitOperations, "CompactTextBoxStyle") >= 4);
        Assert.True(Count(gitOperations, "CompactComboBoxStyle") >= 4);
        Assert.Contains("CompactCheckBoxStyle", gitOperations);
        Assert.Contains("ItemContainerStyle=\"{StaticResource DenseListItemStyle}\"", gitOperations);
        Assert.DoesNotContain("FontWeight=\"SemiBold\"", gitOperations);
        Assert.DoesNotContain("Spacing=\"12\"", gitOperations);
        Assert.DoesNotContain("ColumnSpacing=\"8\"", gitOperations);

        var gitTools = Slice(settings,
            "<ScrollViewer x:Name=\"GitToolsSettingsPanel\"",
            "<ScrollViewer x:Name=\"DiagnosticsSettingsPanel\"");
        Assert.Equal(6, Count(gitTools, "Style=\"{StaticResource CompactComboBoxStyle}\""));
        Assert.Equal(7, Count(gitTools, "Style=\"{StaticResource TechnicalTextBoxStyle}\""));
        Assert.Equal(9, Count(gitTools, "Style=\"{StaticResource CompactButtonStyle}\""));
        Assert.Equal(3, Count(gitTools, "Style=\"{StaticResource CompactCheckBoxStyle}\""));
        Assert.True(Count(gitTools, "Style=\"{StaticResource TechnicalTextStyle}\"") >= 15);
    }

    [Fact]
    public void OperationBannerAndGitConsoleUseSharedCompactAndTechnicalStyles()
    {
        var root = FindRepositoryRoot();
        var banner = Read(root, "src", "CSharpGit.Presentation", "Controls", "OperationBanner.xaml");
        var console = Read(root, "src", "CSharpGit.Presentation", "Controls", "GitConsoleView.xaml");

        Assert.True(Count(banner, "Style=\"{StaticResource CompactButtonStyle}\"") >= 10);
        Assert.Contains("ItemContainerStyle=\"{StaticResource DenseListItemStyle}\"", banner);
        Assert.Contains("BodyStrongTextStyle", banner);
        Assert.DoesNotContain("FontWeight=\"SemiBold\"", banner);

        Assert.Contains("ItemContainerStyle=\"{StaticResource DenseListItemStyle}\"", console);
        Assert.True(Count(console, "TechnicalTextStyle") >= 4);
        Assert.True(Count(console, "TechnicalSecondaryTextStyle") >= 2);
        Assert.Equal(2, Count(console, "TechnicalMultilineTextBoxStyle"));
        Assert.DoesNotContain("FontFamily=\"Consolas\"", console);
        Assert.DoesNotContain("Padding=\"12,8\"", console);
        Assert.DoesNotContain("Spacing=\"8\"", console);
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
            Assert.Contains("SecondaryTextStyle", xaml);
            Assert.Contains("Spacing.", xaml);
        }

        Assert.Contains("SectionHeaderTextStyle", settings);
        Assert.Contains("BodyTextStyle", settings);
        Assert.Contains("CompactComboBoxStyle", settings);
        Assert.Contains("TechnicalTextBoxStyle", settings);
        Assert.Contains("BodySubduedTextStyle", recent);
        Assert.Contains("TechnicalSecondaryTextStyle", recent);
        Assert.DoesNotContain("FontSize=\"18\"", settings);
        Assert.DoesNotContain("FontSize=\"15\"", recent);
        Assert.DoesNotContain("FontSize=\"12\"", recent);
    }

    private static IEnumerable<string> ResourceKeys(string xaml)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Parse(xaml)
            .Descendants()
            .Attributes(x + "Key")
            .Select(attribute => attribute.Value);
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        var end = value.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return value[start..(end + endMarker.Length)];
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
