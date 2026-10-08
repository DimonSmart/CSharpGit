using System.Diagnostics;

namespace CSharpGit.Git.Tests;

internal static class TestDirectory
{
    public static void Delete(string path)
    {
        if (!Directory.Exists(path)) return;
        var watch = Stopwatch.StartNew();
        try
        {
            const int attempts = 6;
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    // The ordinary path is cheap; most repositories have no read-only files.
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    if (!Directory.Exists(path)) return;
                    if (attempt == 1)
                    {
                        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                        {
                            try { File.SetAttributes(file, FileAttributes.Normal); }
                            catch (FileNotFoundException) { }
                            catch (DirectoryNotFoundException) { }
                        }
                    }
                    if (attempt == attempts)
                        throw new IOException($"Failed to clean test directory '{path}' after {attempts} attempts.", error);
                    Thread.Sleep(40 * attempt);
                }
            }
        }
        finally
        {
            GitTestMeasurements.Record("cleanup", "fixture/cleanup", watch.Elapsed);
        }
    }
}
