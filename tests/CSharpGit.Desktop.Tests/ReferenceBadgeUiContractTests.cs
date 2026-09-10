namespace CSharpGit.Desktop.Tests;

public sealed class ReferenceBadgeUiContractTests
{
    [Fact]
    public void GitReferencesUseReusableOutlinedBadgeStyle()
    {
        var root = FindRepositoryRoot();
        var workspace = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml"));
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var designTokens = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "DesignTokens.xaml"));

        Assert.Contains("<Style x:Key=\"ReferenceBadgeStyle\" TargetType=\"Border\">", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"Background\" Value=\"{ThemeResource ControlFillColorDefaultBrush}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"BorderBrush\" Value=\"{ThemeResource ControlStrokeColorDefaultBrush}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"BorderThickness\" Value=\"1\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"CornerRadius\" Value=\"4\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"Padding\" Value=\"{StaticResource Padding.ReferenceBadge}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Property=\"Margin\" Value=\"{StaticResource Margin.ReferenceBadge}\"", workspace, StringComparison.Ordinal);

        Assert.Contains("ItemsSource=\"{Binding Commit.References}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("<Border Style=\"{StaticResource ReferenceBadgeStyle}\">", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0.82\"", workspace, StringComparison.Ordinal);

        Assert.Contains("ItemsSource=\"{Binding SelectedCommit.Commit.References}\"", mainPage, StringComparison.Ordinal);
        Assert.Contains("<Border Style=\"{StaticResource ReferenceBadgeStyle}\">", mainPage, StringComparison.Ordinal);
        Assert.Contains("<x:Double x:Key=\"Height.DataRow\">24</x:Double>", designTokens, StringComparison.Ordinal);
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
