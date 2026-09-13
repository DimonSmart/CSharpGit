namespace CSharpGit.Presentation.Previewing;

internal static class FileLanguageClassifier
{
    private static readonly IReadOnlyDictionary<string, string> Extensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "csharp",
            [".ps1"] = "powershell",
            [".json"] = "json",
            [".xml"] = "xml",
            [".md"] = "markdown",
            [".yml"] = "yaml",
            [".yaml"] = "yaml",
            [".js"] = "javascript",
            [".ts"] = "typescript",
            [".py"] = "python"
        };

    private static readonly IReadOnlyDictionary<string, string> FileNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dockerfile"] = "dockerfile",
            ["Makefile"] = "makefile",
            [".gitignore"] = "gitignore",
            [".editorconfig"] = "editorconfig"
        };

    internal static string? Classify(string gitPath)
    {
        var fileName = Path.GetFileName(gitPath);
        if (FileNames.TryGetValue(fileName, out var byName)) return byName;
        return Extensions.TryGetValue(Path.GetExtension(fileName), out var byExtension) ? byExtension : null;
    }
}
