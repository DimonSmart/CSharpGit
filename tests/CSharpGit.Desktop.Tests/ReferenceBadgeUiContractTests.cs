namespace CSharpGit.Desktop.Tests;

public sealed class ReferenceBadgeUiContractTests
{
    [Fact]
    public void GitReferencesUseReusableOutlinedBadgeStyle()
    {
        var root = FindRepositoryRoot();
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));
        var historyReferences = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml"));
        var presenter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferencesPresenter.cs"));
        var typography = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Typography.xaml"));
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

        Assert.Contains("<controls:HistoryReferencesPresenter", historyReferences, StringComparison.Ordinal);
        Assert.Contains("References=\"{x:Bind Commit.References, Mode=OneWay}\"", historyReferences, StringComparison.Ordinal);
        Assert.DoesNotContain("<ItemsControl", historyReferences, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryReferenceBadge", historyReferences, StringComparison.Ordinal);
        Assert.Contains("ReferenceBadgeStyle", presenter, StringComparison.Ordinal);
        Assert.Contains("ReferenceBadgeTextStyle", presenter, StringComparison.Ordinal);
        Assert.Contains("HistoryReferenceDefaultBranchIconStyle", presenter, StringComparison.Ordinal);
        Assert.Contains("Text=\"reflog\" Style=\"{StaticResource ReferenceBadgeTextStyle}\"", historyReferences, StringComparison.Ordinal);
        Assert.Contains("<Style x:Key=\"ReferenceBadgeTextStyle\" TargetType=\"TextBlock\" BasedOn=\"{StaticResource CaptionTextStyle}\">", typography, StringComparison.Ordinal);
        Assert.Contains("Property=\"Foreground\" Value=\"{ThemeResource TextFillColorPrimaryBrush}\"", typography, StringComparison.Ordinal);
        Assert.Contains("Property=\"Opacity\" Value=\"1\"", typography, StringComparison.Ordinal);
        Assert.Contains("Property=\"FontWeight\" Value=\"SemiBold\"", typography, StringComparison.Ordinal);

        Assert.Contains("<controls:CommitDetailsView x:Name=\"CommitDetailsContent\" />", mainPage, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SelectedHistoryRow.Commit.References}\"", commitDetails, StringComparison.Ordinal);
        Assert.Contains("<Border Style=\"{StaticResource ReferenceBadgeStyle}\">", commitDetails, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding}\" Style=\"{StaticResource ReferenceBadgeTextStyle}\"", commitDetails, StringComparison.Ordinal);
        Assert.Contains("<x:Double x:Key=\"Height.DataRow\">24</x:Double>", designTokens, StringComparison.Ordinal);

        Assert.False(File.Exists(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferenceBadge.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferenceBadge.xaml.cs")));
    }

    [Fact]
    public void HistoryReferencesPresenterOwnsOneBoundedReusableVisualPoolAndOneContextSubscription()
    {
        var root = FindRepositoryRoot();
        var presenter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferencesPresenter.cs"));
        var context = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferencePresentationContext.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryMaintenance.cs"));

        Assert.Contains("public sealed class HistoryReferencesPresenter : Panel", presenter, StringComparison.Ordinal);
        Assert.Contains("private const int MaxSurplusVisuals = 12", presenter, StringComparison.Ordinal);
        Assert.Contains("private readonly List<ReferenceVisual> _visuals", presenter, StringComparison.Ordinal);
        Assert.Contains("HistoryReferencePresentationContext.Changed += PresentationContext_Changed", presenter, StringComparison.Ordinal);
        Assert.Contains("HistoryReferencePresentationContext.Changed -= PresentationContext_Changed", presenter, StringComparison.Ordinal);
        Assert.Contains("HistoryReferencePresentationContext.IsDefaultRemoteBranch", presenter, StringComparison.Ordinal);
        Assert.Contains("HistoryRenderDiagnostics.ReferenceVisualReused()", presenter, StringComparison.Ordinal);
        Assert.Contains("Children.RemoveAt(last)", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsControl", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain(".Select(", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToList(", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper", presenter, StringComparison.Ordinal);
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
