namespace CSharpGit.Git.Tests;

internal static class TestDirectory
{
    public static void Delete(string path)
    {
        if (!Directory.Exists(path)) return;

        const int attempts = 8;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); }
                    catch (FileNotFoundException) { }
                    catch (DirectoryNotFoundException) { }
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                if (attempt == attempts) return;
            }
            catch (IOException)
            {
                if (attempt == attempts) return;
            }

            Thread.Sleep(100 * attempt);
        }
    }
}
