namespace CSharpGit.Desktop.Tests;

public sealed class ReferenceBadgeUiContractTests
{
    [Fact]
    public void GitReferencesUseReusableOutlinedBadgeStyle()
    {
        var root = FindRepositoryRoot();
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));
        var historyReferences = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml"));
        var typography = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Typography.xaml"));
        var historyReferenceBadge = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferenceBadge.xaml"));
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var commitDetails = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitDetailsView.xaml"));
        var designTokens = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "DesignTokens.xaml"));

        Assert.Contains("<Style x:Key=\"ReferenceBadgeStyle\" TargetType=\"Border\">", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"Background\" Value=\"{ThemeResource ControlSolidFillColorDefaultBrush}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"BorderBrush\" Value=\"{ThemeResource ControlStrongStrokeColorDefaultBrush}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"BorderThickness\" Value=\"1\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"CornerRadius\" Value=\"4\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"Padding\" Value=\"{StaticResource Padding.ReferenceBadge}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"Margin\" Value=\"{StaticResource Margin.ReferenceBadge}\"", workspace, StringComparison.Ordinal);

        Assert.Contains("ItemsSource=\"{Binding Commit.References}\"", historyReferences, StringComparison.Ordinal);
        Assert.Contains("<Border Style=\"{StaticResource ReferenceBadgeStyle}\">", historyReferences, StringComparison.Ordinal);
        Assert.Contains("Text=\"reflog\" Style=\"{StaticResource ReferenceBadgeTextStyle}\"", historyReferences, StringComparison.Ordinal);
        Assert.Contains("<Style x:Key=\"ReferenceBadgeTextStyle\" TargetType=\"TextBlock\" BasedOn=\"{StaticResource CaptionTextStyle}\">", typography, StringComparison.Ordinal);
        Assert.Contains("Property=\"Foreground\" Value=\"{ThemeResource TextFillColorPrimaryBrush}\"", typography, StringComparison.Ordinal);
        Assert.Contains("Property=\"Opacity\" Value=\"1\"", typography, StringComparison.Ordinal);
        Assert.Contains("Property=\"FontWeight\" Value=\"SemiBold\"", typography, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource ReferenceBadgeTextStyle}\"", historyReferenceBadge, StringComparison.Ordinal);

        Assert.Contains("<controls:CommitDetailsView x:Name=\"CommitDetailsContent\" />", mainPage, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SelectedHistoryRow.Commit.References}\"", commitDetails, StringComparison.Ordinal);
        Assert.Contains("<Border Style=\"{StaticResource ReferenceBadgeStyle}\">", commitDetails, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding}\" Style=\"{StaticResource ReferenceBadgeTextStyle}\"", commitDetails, StringComparison.Ordinal);
        Assert.Contains("<x:Double x:Key=\"Height.DataRow\">24</x:Double>", designTokens, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryReferenceBadgeUsesSharedConstantTimeDefaultBranchContext()
    {
        var root = FindRepositoryRoot();
        var badge = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferenceBadge.xaml.cs"));
        var context = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferencePresentationContext.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryMaintenance.cs"));

        Assert.Contains("HistoryReferencePresentationContext.IsDefaultRemoteBranch", badge, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper", badge, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoteBranches.Any", badge, StringComparison.Ordinal);
        Assert.Contains("HashSet<string>", context, StringComparison.Ordinal);
        Assert.Contains(".ToHashSet(StringComparer.Ordinal)", context, StringComparison.Ordinal);
        Assert.Contains("RemoteBranches.CollectionChanged += RemoteBranches_CollectionChanged", context, StringComparison.Ordinal);
        Assert.Contains("HistoryReferencePresentationContext.Configure(_viewModel)", composition, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
