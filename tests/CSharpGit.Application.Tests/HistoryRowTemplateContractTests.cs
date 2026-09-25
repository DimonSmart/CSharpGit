using System.Xml.Linq;

namespace CSharpGit.Application.Tests;

public sealed class HistoryRowTemplateContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void HistoryRowStyleUsesMinimalCustomContainerTemplate()
    {
        var style = LoadHistoryRowStyle();

        Assert.Null(style.Attribute("BasedOn"));
        Assert.Equal("ListViewItem", (string?)style.Attribute("TargetType"));
        AssertSetter(style, "MinHeight", "{StaticResource Height.DataRow}");
        AssertSetter(style, "Margin", "0");
        AssertSetter(style, "Padding", "0");
        AssertSetter(style, "HorizontalContentAlignment", "Stretch");
        AssertSetter(style, "VerticalContentAlignment", "Stretch");
        AssertSetter(style, "UseSystemFocusVisuals", "True");

        var template = style
            .Descendants(Presentation + "ControlTemplate")
            .Single();

        Assert.Equal("ListViewItem", (string?)template.Attribute("TargetType"));

        var root = template.Elements(Presentation + "Border").Single();
        Assert.Equal("Root", (string?)root.Attribute(Xaml + "Name"));

        var presenter = root.Elements(Presentation + "ContentPresenter").Single();
        Assert.Equal("{TemplateBinding Content}", (string?)presenter.Attribute("Content"));
        Assert.Equal("{TemplateBinding ContentTemplate}", (string?)presenter.Attribute("ContentTemplate"));
        Assert.Equal("{TemplateBinding ContentTemplateSelector}", (string?)presenter.Attribute("ContentTemplateSelector"));
        Assert.Equal("{TemplateBinding Padding}", (string?)presenter.Attribute("Padding"));
        Assert.Equal("{TemplateBinding HorizontalContentAlignment}", (string?)presenter.Attribute("HorizontalContentAlignment"));
        Assert.Equal("{TemplateBinding VerticalContentAlignment}", (string?)presenter.Attribute("VerticalContentAlignment"));

        var stateNames = root
            .Descendants(Presentation + "VisualState")
            .Select(state => (string?)state.Attribute(Xaml + "Name"))
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Normal", stateNames);
        Assert.Contains("PointerOver", stateNames);
        Assert.Contains("Selected", stateNames);
        Assert.Contains("PointerOverSelected", stateNames);

        var templateText = template.ToString(SaveOptions.DisableFormatting);
        Assert.DoesNotContain("ListViewItemPresenter", templateText, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard", templateText, StringComparison.Ordinal);
        Assert.DoesNotContain("DoubleAnimation", templateText, StringComparison.Ordinal);
        Assert.DoesNotContain("ColorAnimation", templateText, StringComparison.Ordinal);
        Assert.DoesNotContain("Transition", templateText, StringComparison.Ordinal);
        Assert.DoesNotContain("ListViewItemExpanded", templateText, StringComparison.Ordinal);
        Assert.DoesNotContain("DenseListItemStyle", templateText, StringComparison.Ordinal);
    }

    private static XElement LoadHistoryRowStyle()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "Workspace.xaml");
        var document = XDocument.Load(path);

        return document
            .Descendants(Presentation + "Style")
            .Single(style => string.Equals(
                (string?)style.Attribute(Xaml + "Key"),
                "HistoryRowStyle",
                StringComparison.Ordinal));
    }

    private static void AssertSetter(XElement style, string property, string expectedValue)
    {
        var setter = style
            .Elements(Presentation + "Setter")
            .Single(element => string.Equals(
                (string?)element.Attribute("Property"),
                property,
                StringComparison.Ordinal));

        Assert.Equal(expectedValue, (string?)setter.Attribute("Value"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
