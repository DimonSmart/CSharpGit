using System.Xml.Linq;
using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;
using CSharpGit.Presentation.Threading;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Application.Tests;

public sealed class CoreAssemblyArchitectureTests
{
    [Fact]
    public void ProductionTypesAreLoadedFromTheirRealSingleCoreOwners()
    {
        Assert.Equal("CSharpGit.Infrastructure.Core", typeof(JsonAppSettingsService).Assembly.GetName().Name);
        Assert.Equal("CSharpGit.Infrastructure.Core", typeof(RepositoryImageService).Assembly.GetName().Name);
        Assert.Equal("CSharpGit.Presentation.Core", typeof(IUiDispatcher).Assembly.GetName().Name);
        Assert.Equal("CSharpGit.Presentation.Core", typeof(RepositorySyncViewModel).Assembly.GetName().Name);
        Assert.Equal("CSharpGit.Presentation.Core", typeof(RepositoryMaintenanceViewModel).Assembly.GetName().Name);
        Assert.Equal("CSharpGit.Presentation.Core", typeof(FileActionsViewModel).Assembly.GetName().Name);
        foreach (var assembly in new[] { typeof(JsonAppSettingsService).Assembly, typeof(RepositorySyncViewModel).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name is "CSharpGit" or "CSharpGit.Infrastructure" or "Uno.UI" or "Microsoft.UI.Xaml");
    }

    [Fact]
    public void Net10TestsDoNotCompileOrCopyProductionSources()
    {
        var root = Root();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories))
        {
            var project = XDocument.Load(file);
            var projectDirectory = Path.GetDirectoryName(file)!;
            foreach (var source in project.Descendants("Compile"))
            {
                var include = source.Attribute("Include")?.Value;
                if (include is null) continue;
                var resolved = Path.GetFullPath(Path.Combine(projectDirectory, include.Replace('/', Path.DirectorySeparatorChar)));
                Assert.False(resolved.StartsWith(Path.Combine(root, "src") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                    $"Test project compiles production source: {file}: {include}");
            }
        }
    }

    [Fact]
    public void CompileOwnershipIsUniqueEvenForLinkedSources()
    {
        var root = Root();
        foreach (var (coreName, adapterName, expected) in new[]
        {
            ("CSharpGit.Infrastructure.Core", "CSharpGit.Infrastructure", 11),
            ("CSharpGit.Presentation.Core", "CSharpGit.Presentation", 81)
        })
        {
            var corePath = Path.Combine(root, "src", coreName, coreName + ".csproj");
            var adapterPath = Path.Combine(root, "src", adapterName, adapterName + ".csproj");
            var core = XDocument.Load(corePath);
            var adapter = XDocument.Load(adapterPath);
            Assert.Equal("Microsoft.NET.Sdk", core.Root?.Attribute("Sdk")?.Value);
            Assert.Equal("net10.0", core.Descendants("TargetFramework").Single().Value);
            var linked = core.Descendants("Compile")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => value is not null && value.StartsWith("../", StringComparison.Ordinal))
                .Select(value => value!).ToArray();
            Assert.Equal(expected, linked.Length);
            Assert.Equal(linked.Length, linked.Distinct(StringComparer.Ordinal).Count());
            var excluded = adapter.Descendants("Compile").Select(x => x.Attribute("Remove")?.Value)
                .Where(x => x is not null).ToHashSet(StringComparer.Ordinal);
            foreach (var source in linked)
            {
                var name = source.Substring(("../" + adapterName + "/").Length);
                Assert.StartsWith("../" + adapterName + "/", source, StringComparison.Ordinal);
                Assert.Contains(name, excluded);
                Assert.True(File.Exists(Path.GetFullPath(Path.Combine(root, "src", coreName, source))));
            }
            var references = core.Descendants("ProjectReference").Select(x => x.Attribute("Include")?.Value ?? "").ToArray();
            Assert.DoesNotContain(references, value => value.Contains("Presentation/", StringComparison.Ordinal) ||
                value.Contains("Infrastructure/", StringComparison.Ordinal) || value.Contains("Uno", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ProductionProjectReferencesRemainAcyclicAndDirectional()
    {
        var root = Root();
        var source = Path.Combine(root, "src");
        var projects = Directory.GetFiles(source, "*.csproj", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetFileNameWithoutExtension(path),
            path => XDocument.Load(path).Descendants("ProjectReference")
                .Select(x => Path.GetFileNameWithoutExtension(x.Attribute("Include")!.Value.Replace('\\', '/')))
                .ToArray());
        var allowed = new Dictionary<string, string[]>
        {
            ["CSharpGit.Domain"] = [],
            ["CSharpGit.Application"] = ["CSharpGit.Domain"],
            ["CSharpGit.Git"] = ["CSharpGit.Application", "CSharpGit.Domain"],
            ["CSharpGit.Infrastructure.Core"] = ["CSharpGit.Application", "CSharpGit.Domain"],
            ["CSharpGit.Presentation.Core"] = ["CSharpGit.Application", "CSharpGit.Domain"],
            ["CSharpGit.Infrastructure"] = ["CSharpGit.Application", "CSharpGit.Domain", "CSharpGit.Infrastructure.Core"]
        };
        foreach (var (name, valid) in allowed)
        {
            Assert.True(projects.ContainsKey(name));
            foreach (var reference in projects[name])
                Assert.Contains(reference, valid);
        }

        var current = new HashSet<string>(StringComparer.Ordinal);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string name)
        {
            Assert.True(current.Add(name), $"Cyclic ProjectReference at {name}");
            foreach (var reference in projects[name])
            {
                Assert.True(projects.ContainsKey(reference), $"Missing referenced project {reference}");
                if (!completed.Contains(reference)) Visit(reference);
            }
            current.Remove(name);
            completed.Add(name);
        }
        foreach (var name in projects.Keys)
            if (!completed.Contains(name)) Visit(name);
    }

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit solution root not found.");
    }
}
