using System.Xml.Linq;

namespace CSharpGit.Application.Tests;

public sealed class XamlOnlyMigrationContractTests
{
    [Fact]
    public void MainPageIsDeclarativeAndHasNoAuthoredPartialClass()
    {
        var presentation = Path.Combine(FindRepositoryRoot(), "src", "CSharpGit.Presentation");
        var document = XDocument.Load(Path.Combine(presentation, "MainPage.xaml"));
        Assert.Equal("Page", document.Root!.Name.LocalName);
        Assert.Empty(Directory.GetFiles(presentation, "MainPage*.cs", SearchOption.TopDirectoryOnly));
        foreach (var path in Directory.EnumerateFiles(presentation, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(presentation, path);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin")) continue;
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(path), @"\bpartial\s+class\s+MainPage\b"), relative);
        }
        var eventNames = new[] { "Loaded", "Unloaded", "KeyDown", "Click", "SelectionChanged", "Tapped", "RightTapped" };
        Assert.DoesNotContain(document.Root.DescendantsAndSelf().Attributes(), attribute => eventNames.Contains(attribute.Name.LocalName));
    }

    [Fact]
    public void ConsolidatedWorkspaceArchitectureIntentExists()
    {
        var intent = Path.Combine(FindRepositoryRoot(), ".idd", "intent",
            "IDD-0047.adr-ui-composition-and-window-lifetime.md");
        Assert.True(File.Exists(intent), intent);
        var content = File.ReadAllText(intent);
        Assert.Contains("MainPage.xaml is the declarative visual root", content, StringComparison.Ordinal);
        Assert.Contains("WindowWorkspaceComposition explicitly wires", content, StringComparison.Ordinal);
        Assert.Contains("CSHARPGIT_REQUIRE_DESKTOP_CHECK=1", content, StringComparison.Ordinal);
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
