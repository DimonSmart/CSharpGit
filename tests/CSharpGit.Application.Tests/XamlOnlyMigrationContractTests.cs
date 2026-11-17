using System.Xml.Linq;

namespace CSharpGit.Application.Tests;

public sealed class XamlOnlyMigrationContractTests
{
    [Fact]
    public void ProbeHasBindingAndNoAuthoredCodeBehind()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation", "Views");
        var path = Path.Combine(presentation, "XamlOnlyProbe.xaml");
        var document = XDocument.Load(path);
        var xClass = XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml");
        Assert.Equal("UserControl", document.Root!.Name.LocalName);
        Assert.Equal("CSharpGit.Presentation.Views.XamlOnlyProbe", document.Root.Attribute(xClass)?.Value);
        Assert.False(File.Exists(path + ".cs"));
        Assert.Contains(document.Descendants(), x => (string?)x.Attribute("Text") == "{Binding ProbeText}");
    }

    [Fact]
    public void PhaseOneArchitectureDocumentsExist()
    {
        var architecture = Path.Combine(FindRepositoryRoot(), "docs", "architecture");
        Assert.True(File.Exists(Path.Combine(architecture, "mainpage-migration-map.md")));
        Assert.True(File.Exists(Path.Combine(architecture, "ui-composition.md")));
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
