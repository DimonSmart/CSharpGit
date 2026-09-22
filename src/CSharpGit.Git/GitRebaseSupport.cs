using CSharpGit.Domain;

namespace CSharpGit.Git;

internal static class GitRebaseSupport
{
    internal static string DirectoryPath(Repository repository) =>
        Path.Combine(repository.GitDirectory, "csharpgit-rebase");

    internal static string? ReadRewordMarker(Repository repository, int index)
    {
        var path = Path.Combine(
            DirectoryPath(repository),
            $"reword-{index}.sha");
        if (!File.Exists(path)) return null;
        var value = File.ReadAllText(path).Trim();
        return value.Length == 0 ? null : value;
    }

    internal static void Cleanup(Repository repository)
    {
        var path = DirectoryPath(repository);
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
