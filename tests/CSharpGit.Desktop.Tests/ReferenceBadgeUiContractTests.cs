namespace CSharpGit.Desktop.Tests;

public sealed class ReferenceBadgeUiContractTests
{
    [Fact]
    public void GitReferencesUseReusableTypedSemanticBadges()
    {
        var root = FindRepositoryRoot();
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));
        var historyReferences = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml"));
        var presenter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferencesPresenter.cs"));
        var domain = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Domain", "History.cs"));
        var typography = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Typography.xaml"));
        var designTokens = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "DesignTokens.xaml"));

        Assert.Contains("<Style x:Key=\"ReferenceBadgeStyle\" TargetType=\"Border\">", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"BorderThickness\" Value=\"1\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"CornerRadius\" Value=\"4\"", workspace, StringComparison.Ordinal);
        Assert.Contains("<controls:HistoryReferencesPresenter", historyReferences, StringComparison.Ordinal);
        Assert.Contains("References=\"{x:Bind ReferenceDetails, Mode=OneWay}\"", historyReferences, StringComparison.Ordinal);
        Assert.Contains("HistoryReferenceKindMarkerStyle", historyReferences, StringComparison.Ordinal);
        Assert.Contains("HistoryReferenceTrackingTextStyle", historyReferences, StringComparison.Ordinal);
        Assert.Contains("HistoryReferenceDecoration", domain, StringComparison.Ordinal);
        Assert.Contains("HistoryReferenceKind.CurrentLocalBranch", presenter, StringComparison.Ordinal);
        Assert.Contains("HistoryReferenceKind.LocalBranch", presenter, StringComparison.Ordinal);
        Assert.Contains("HistoryReferenceKind.RemoteTrackingBranch", presenter, StringComparison.Ordinal);
        Assert.Contains("HistoryReferenceKind.Tag", presenter, StringComparison.Ordinal);
        Assert.Contains("HistoryReferenceKind.DetachedHead", presenter, StringComparison.Ordinal);
        Assert.Contains("· upstream", presenter, StringComparison.Ordinal);
        Assert.Contains("remote-tracking reference", presenter, StringComparison.Ordinal);
        Assert.Contains("<Style x:Key=\"ReferenceBadgeTextStyle\" TargetType=\"TextBlock\" BasedOn=\"{StaticResource CaptionTextStyle}\">", typography, StringComparison.Ordinal);
        Assert.Contains("<x:Double x:Key=\"Height.DataRow\">24</x:Double>", designTokens, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryReferencesPresenterOwnsOneBoundedReusableVisualPoolWithoutDisplayNameHeuristics()
    {
        var root = FindRepositoryRoot();
        var presenter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferencesPresenter.cs"));

        Assert.Contains("public sealed class HistoryReferencesPresenter : Panel", presenter, StringComparison.Ordinal);
        Assert.Contains("private const int MaxSurplusVisuals = 12", presenter, StringComparison.Ordinal);
        Assert.Contains("private readonly List<ReferenceVisual> _visuals", presenter, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<HistoryReferenceDecoration>", presenter, StringComparison.Ordinal);
        Assert.Contains("HistoryRenderDiagnostics.ReferenceVisualReused()", presenter, StringComparison.Ordinal);
        Assert.Contains("Children.RemoveAt(last)", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryReferencePresentationContext", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("origin/", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("tag:", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper", presenter, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryReferencesPresenterKeepsBadgesInsideMessageColumnAndSummarizesOverflow()
    {
        var root = FindRepositoryRoot();
        var presenter = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "HistoryReferencesPresenter.cs"));

        Assert.Contains("private readonly RectangleGeometry _clip = new()", presenter, StringComparison.Ordinal);
        Assert.Contains("Clip = _clip;", presenter, StringComparison.Ordinal);
        Assert.Contains("_overflowMeasureText.Text = maximumOverflowText", presenter, StringComparison.Ordinal);
        Assert.Contains("totalWidth > finalSize.Width", presenter, StringComparison.Ordinal);
        Assert.Contains("Math.Max(0, finalSize.Width - overflowWidth)", presenter, StringComparison.Ordinal);
        Assert.Contains("GetVisibleReferenceCount(availableForReferences)", presenter, StringComparison.Ordinal);
        Assert.Contains("_overflowText.Text = $\"+{hiddenCount}\"", presenter, StringComparison.Ordinal);
        Assert.Contains("references[index].DisplayName", presenter, StringComparison.Ordinal);
        Assert.Contains("child.Arrange(new Rect(0, 0, 0, 0))", presenter, StringComparison.Ordinal);
        Assert.Contains("SetReferenceVisibility(child, false)", presenter, StringComparison.Ordinal);
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
