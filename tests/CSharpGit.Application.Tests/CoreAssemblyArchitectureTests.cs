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
        Assert.Equal("CSharpGit.Presentation.Core", typeof(HistoryViewModel).Assembly.GetName().Name);
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
    public void CoreSourcesArePhysicallyOwnedAndCompiledByOneProject()
    {
        var root = Root();
        var sourcesRoot = Path.Combine(root, "src");
        var coreProjects = new[]
        {
            ("CSharpGit.Infrastructure.Core", "CSharpGit.Infrastructure"),
            ("CSharpGit.Presentation.Core", "CSharpGit.Presentation")
        };
        foreach (var (coreName, adapterName) in coreProjects)
        {
            var coreDirectory = Path.Combine(sourcesRoot, coreName);
            var adapterDirectory = Path.Combine(sourcesRoot, adapterName);
            var core = XDocument.Load(Path.Combine(coreDirectory, coreName + ".csproj"));
            var adapter = XDocument.Load(Path.Combine(adapterDirectory, adapterName + ".csproj"));

            Assert.Equal("Microsoft.NET.Sdk", core.Root?.Attribute("Sdk")?.Value);
            Assert.Equal("net10.0", core.Descendants("TargetFramework").Single().Value);
            Assert.DoesNotContain(core.Descendants("EnableDefaultCompileItems"),
                setting => string.Equals(setting.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(core.Descendants("Compile"), item => item.Attribute("Include") is not null);
            Assert.DoesNotContain(core.Descendants("Compile"), item => item.Attribute("Remove") is not null);

            var coreSourceFiles = Directory.EnumerateFiles(coreDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.NotEmpty(coreSourceFiles);
            foreach (var source in coreSourceFiles)
            {
                var relative = Path.GetRelativePath(coreDirectory, source);
                Assert.False(relative.StartsWith("..", StringComparison.Ordinal));
                Assert.True(File.Exists(source));
                Assert.False(File.Exists(Path.Combine(adapterDirectory, relative)),
                    $"Core source is duplicated in Uno: {source}");
                Assert.DoesNotContain(adapter.Descendants("Compile"),
                    item => string.Equals(item.Attribute("Remove")?.Value?.Replace('\\', '/'),
                        relative.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            }

            var references = core.Descendants("ProjectReference")
                .Select(item => item.Attribute("Include")?.Value ?? "")
                .ToArray();
            Assert.DoesNotContain(references, value =>
                value.Contains("Presentation/", StringComparison.Ordinal) ||
                value.Contains("Infrastructure/", StringComparison.Ordinal) ||
                value.Contains("Uno", StringComparison.Ordinal));
            Assert.Contains(adapter.Descendants("ProjectReference"), reference =>
                reference.Attribute("Include")?.Value.Replace('\\', '/')
                    .EndsWith("/" + coreName + ".csproj", StringComparison.Ordinal) == true);
        }

        // No other production project may link external source files back into its compilation.
        foreach (var projectPath in Directory.GetFiles(sourcesRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var project = XDocument.Load(projectPath);
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            foreach (var compile in project.Descendants("Compile"))
            {
                var include = compile.Attribute("Include")?.Value;
                if (include is null || include.Contains('*') || include.Contains('?') || include.Contains("$("))
                    continue;
                var fullPath = Path.GetFullPath(Path.Combine(projectDirectory,
                    include.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(fullPath), $"Missing compile source: {projectPath}: {include}");
                Assert.True(fullPath.StartsWith(projectDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
                    $"Production source compiled outside its project: {projectPath}: {include}");
            }
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
