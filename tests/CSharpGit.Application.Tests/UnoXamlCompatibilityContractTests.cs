namespace CSharpGit.Application.Tests;

public sealed class UnoXamlCompatibilityContractTests
{
    [Fact]
    public void DenseListItemPresenterAvoidsUnsupportedUnoProperties()
    {
        var root = FindRepositoryRoot();
        var workspace = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CSharpGit.Presentation",
            "Styles",
            "Workspace.xaml"));

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

        Assert.Contains("<primitives:ListViewItemPresenter", workspace);
        Assert.Contains("Padding=\"{TemplateBinding Padding}\"", workspace);
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
