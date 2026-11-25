namespace CSharpGit.Application.Tests;

public sealed class CommitMetadataCopyUiContractTests
{
    [Fact]
    public void CommitDetailsExposeExactMetadataCopyActions()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitDetailsView.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitDetailsView.xaml.cs"));
        var mainPageXaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Views", "HistoryWorkspaceView.xaml"));
        var wiring = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Workflows", "CommitDetailsLayoutController.cs"));
        var lifecycle = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.Lifecycle.cs"));
        var history = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Domain", "History.cs"));

        Assert.Contains("Tag=\"{Binding History.SelectedRow.Commit.Message}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"Copy commit message\"", xaml);
        Assert.Contains("Tag=\"{Binding History.SelectedRow.Commit.Hash}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"Copy commit hash\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding History.SelectedRow.Commit.Parents}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"Copy parent hash\"", xaml);
        Assert.Contains("History.SelectedRow.Commit.IsRootCommit", xaml);
        Assert.Contains("Clipboard.SetContent(package)", codeBehind);
        Assert.Contains("icon.Glyph = \"\\uE73E\"", codeBehind);
        Assert.Contains("<controls:CommitDetailsView x:Name=\"CommitDetailsContent\" />", mainPageXaml);
        Assert.DoesNotContain("new CommitDetailsView", wiring);
        Assert.DoesNotContain("DetailsScroller.Content =", wiring);
        Assert.Contains("_commitDetailsLayout.Initialize();", lifecycle);
        Assert.Contains("public bool IsRootCommit => Parents.Count == 0;", history);
        Assert.Contains("IsRootCommit ? \"—\"", history);
    }

    [Fact]
    public void AuthorEmailIsSelectableSecondaryTextWithExactCopyAction()
    {
        var root = FindRepositoryRoot();
        var controls = Path.Combine(root, "src", "CSharpGit.Presentation", "Controls");
        var xaml = System.Xml.Linq.XDocument.Load(Path.Combine(controls, "CommitDetailsView.xaml"));
        var ns = xaml.Root!.Name.Namespace;
        const string emailBinding = "{Binding History.SelectedRow.Commit.AuthorEmail}";
        var email = Assert.Single(xaml.Descendants(ns + "TextBlock")
            .Where(element => (string?)element.Attribute("Text") == emailBinding));
        var row = email.Parent!;
        var copy = Assert.Single(row.Elements(ns + "Button"));
        var author = Assert.Single(row.Parent!.Elements(ns + "TextBlock"));

        Assert.Equal("{Binding History.SelectedRow.Commit.Author}", (string?)author.Attribute("Text"));
        Assert.Equal("{StaticResource SelectableSecondaryTextStyle}", (string?)email.Attribute("Style"));
        Assert.Equal("Wrap", (string?)email.Attribute("TextWrapping"));
        Assert.Equal("*,Auto", (string?)row.Attribute("ColumnDefinitions"));
        Assert.Equal("{Binding History.SelectedRow.Commit.AuthorEmail, Converter={StaticResource NonEmptyStringToVisibilityConverter}, FallbackValue=Collapsed}",
            (string?)row.Attribute("Visibility"));
        Assert.Equal(emailBinding, (string?)copy.Attribute("Tag"));
        Assert.Equal("1", (string?)copy.Attribute("Grid.Column"));
        Assert.Equal("CopyText_Click", (string?)copy.Attribute("Click"));
        Assert.Equal("{StaticResource InlineCopyButtonStyle}", (string?)copy.Attribute("Style"));
        Assert.Equal("Copy author email", (string?)copy.Attribute("ToolTipService.ToolTip"));
        Assert.Equal("Copy author email", (string?)copy.Attribute("AutomationProperties.Name"));

        var converter = File.ReadAllText(Path.Combine(controls, "NonEmptyStringToVisibilityConverter.cs"));
        Assert.Contains("!string.IsNullOrWhiteSpace(value as string)", converter);
        Assert.Contains("Visibility.Visible : Visibility.Collapsed", converter);
        var codeBehind = File.ReadAllText(Path.Combine(controls, "CommitDetailsView.xaml.cs"));
        Assert.Contains("Button { Tag: string text }", codeBehind);
        Assert.Contains("package.SetText(text)", codeBehind);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
