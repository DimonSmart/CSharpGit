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
        var icons = Read(root, "src", "CSharpGit.Presentation", "Styles", "Icons.xaml");
        var workspace = Read(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml");
        var repositoryTree = Read(root, "src", "CSharpGit.Presentation", "Styles", "RepositoryTree.xaml");
        var historyReferences = Read(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml");

        var sources = new[]
        {
            "Styles/DesignTokens.xaml",
            "Styles/Icons.xaml",
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
            "TechnicalTextStyle", "TechnicalSecondaryTextStyle", "DiffTextStyle",
            "SelectableBodyTextStyle", "SelectableSecondaryTextStyle",
            "SelectableTechnicalTextStyle", "SelectableDiffTextStyle"
        })
            Assert.Contains($"x:Key=\"{style}\"", typography);

        foreach (var style in new[]
        {
            "CompactButtonStyle", "IconButtonStyle", "CompactTextBoxStyle", "CompactMultilineTextBoxStyle",
            "TechnicalTextBoxStyle", "TechnicalMultilineTextBoxStyle",
            "CompactComboBoxStyle", "CompactCheckBoxStyle", "CompactRadioButtonStyle"
        })
            Assert.Contains($"x:Key=\"{style}\"", controls);

        Assert.Contains("BasedOn=\"{StaticResource TechnicalTextStyle}\"", typography);
        Assert.Contains("BasedOn=\"{StaticResource CompactButtonStyle}\"", controls);
        Assert.Contains("Property=\"CornerRadius\" Value=\"4\"", controls);
        Assert.Contains("<Style x:Key=\"DenseListItemStyle\"", workspace);
        Assert.Contains("BasedOn=\"{StaticResource ListViewItemExpanded}\"", workspace);
        Assert.Contains("BasedOn=\"{StaticResource DenseTreeItemStyle}\"", repositoryTree);

        var dictionaries = new Dictionary<string, string>
        {
            ["DesignTokens.xaml"] = tokens,
            ["Icons.xaml"] = icons,
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
    public void DynamicallyNamedDetailsTabsRetainSharedStyleAndLazySelection()
    {
        var root = FindRepositoryRoot();
        var files = Read(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryFiles.cs");
        var changes = Read(root, "src", "CSharpGit.Presentation", "MainPage.Changes.cs");
        var mainCode = Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml.cs");

        Assert.Contains("new PivotItem { Header = \"Files\", Name = \"FilesTab\" }", files);
        Assert.Contains("DetailsTabs.Items.Add(_repositoryFilesTab)", files);
        Assert.DoesNotContain("_repositoryFilesTab.HeaderTemplate =", files);
        Assert.DoesNotContain("_repositoryFilesTab.Style =", files);
        Assert.Contains("DetailsTabs.SelectedItem", files);
        Assert.Contains("SetActiveAsync(true)", files);
        Assert.Contains("SetActiveAsync(false)", files);
        Assert.Contains("ActivateRepositoryFilesAsync", files);
        Assert.Contains("DetailsTabs.SelectedItem", changes);
        Assert.Contains("SetActive(active)", changes);
        Assert.Contains("UpdateRepositoryFilesViewActivity();", changes);
        Assert.Contains("UpdateChangesViewActivity();", changes);
        Assert.Contains("Changes ({_commitFiles.Count})", changes);
        Assert.Contains("\"Tracked files\"", mainCode);
        Assert.Contains("\"Stash\"", mainCode + changes + files + 
            Read(root, "src", "CSharpGit.Presentation.Core", "ViewModels", "CommitDetailsViewModel.cs"));
    }

    [Fact]
    public void WorkingTreeAndFileActionsHaveASingleXamlOwnerAndSharedIcons()
    {
        var root = FindRepositoryRoot();
        var main = XDocument.Parse(Read(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var icons = XDocument.Parse(Read(root, "src", "CSharpGit.Presentation", "Styles", "Icons.xaml"));
        var code = Read(root, "src", "CSharpGit.Presentation", "MainPage.FileOpening.cs");
        var menu = Read(root, "src", "CSharpGit.Presentation", "MainPage.WorkingTreeContextMenu.cs");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement Named(string name) => Assert.Single(main.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == name);
        var semanticIcons = icons.Descendants()
            .Where(element => element.Attribute(x + "Key") is not null)
            .ToDictionary(element => (string)element.Attribute(x + "Key")!, element => element.Value);
        Assert.NotEqual(semanticIcons["Icon.Glyph.Fetch"], semanticIcons["Icon.Glyph.Pull"]);

        foreach (var (header, commands) in new[]
        {
            ("UnstagedHeader", new[] { "StageAllCommand", "RequestDiscardAllCommand" }),
            ("StagedHeader", new[] { "UnstageAllCommand" })
        })
        {
            var buttons = Named(header).Parent!.Descendants()
                .Where(element => element.Name.LocalName == "Button").ToArray();
            Assert.Equal(commands.Length, buttons.Length);
            foreach (var command in commands)
            {
                var button = Assert.Single(buttons, element =>
                    ((string?)element.Attribute("Command"))?.Contains(command, StringComparison.Ordinal) == true);
                var expectedStyle = command == "RequestDiscardAllCommand"
                    ? "{StaticResource DestructiveActionButtonStyle}"
                    : "{StaticResource CompactButtonStyle}";
                Assert.Equal(expectedStyle, (string?)button.Attribute("Style"));
                Assert.NotNull(button.Attribute("AutomationProperties.Name"));
                Assert.Single(button.Descendants(), element => element.Name.LocalName == "FontIcon");
            }
        }

        foreach (var scope in new[] { "Commit", "WorkingTree" })
        {
            foreach (var action in new[] { "OpenOriginal", "OpenChanged", "ExternalDiff", "Reveal" })
            {
                var button = Named(scope + action + "Button");
                Assert.Equal(scope + action + "_Click", (string?)button.Attribute("Click"));
                Assert.Equal("{StaticResource CompactButtonStyle}", (string?)button.Attribute("Style"));
                Assert.NotNull(button.Attribute("AutomationProperties.Name"));
                var glyph = (string?)Assert.Single(button.Descendants(), element => element.Name.LocalName == "FontIcon").Attribute("Glyph");
                Assert.StartsWith("{StaticResource Icon.Glyph.", glyph);
            }
        }

        Assert.DoesNotContain("InstallWorkingTreeBulkActions", code);
        Assert.DoesNotContain("InstallCommitFileActions", code);
        Assert.DoesNotContain("InstallWorkingTreeFileActions", code);
        Assert.DoesNotContain("Children.Clear()", code);
        Assert.DoesNotContain("CreateActionButton(", code);
        Assert.Contains("WorkingTree.StageSelectedCommand", menu);
        Assert.Contains("WorkingTree.UnstageSelectedCommand", menu);
        Assert.Contains("WorkingTree.RequestDiscardSelectedCommand", menu);
        Assert.Contains("Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)", code);
    }

    [Fact]
    public void RuntimeDialogsResolveCanonicalResourcesWithoutDuplicatingStyleValues()
    {
        var root = FindRepositoryRoot();
        var resolver = Read(root, "src", "CSharpGit.Presentation", "UiStyles.cs");
        var styles = Read(root, "src", "CSharpGit.Presentation", "Styles", "Controls.xaml");
        Assert.Contains("MergedDictionaries", resolver);
        Assert.Contains("TryFind", resolver);
        Assert.Contains("CompactRadioButtonStyle", styles);

        foreach (var file in new[]
        {
            "MainPage.RepositoryCloning.cs", "MainPage.RepositoryCreation.cs",
            "MainPage.ForcePush.cs", "MainPage.Stashes.cs", "MainPage.Worktrees.cs",
            "MainPage.BranchDeletion.cs", "MainPage.Tags.cs", "MainPage.About.cs"
        })
        {
            var dialog = Read(root, "src", "CSharpGit.Presentation", file);
            Assert.Contains("UiStyles.Resolve<Style>", dialog);
        }
    }

    [Fact]
    public void CommitGraphLayoutDoesNotOwnHistoryRowHeight()
    {
        var root = FindRepositoryRoot();
        var metrics = Read(root, "src", "CSharpGit.Presentation.Core", "Controls", "CommitGraph", "CommitGraphMetrics.cs");
        var control = Read(root, "src", "CSharpGit.Presentation", "Controls", "CommitGraph", "CommitGraphControl.cs");
        var builder = Read(root, "src", "CSharpGit.Presentation.Core", "Controls", "CommitGraph", "CommitGraphGeometryBuilder.cs");

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

    private static IEnumerable<string> ResourceKeys(string xaml)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Parse(xaml)
            .Descendants()
            .Attributes(x + "Key")
            .Select(attribute => attribute.Value);
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
