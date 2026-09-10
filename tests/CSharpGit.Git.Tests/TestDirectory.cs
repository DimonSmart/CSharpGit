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
            catch (UnauthorizedAccessException) when (attempt < attempts) { }
            catch (IOException) when (attempt < attempts) { }

            Thread.Sleep(100 * attempt);
        }

        // Cleanup must not turn a successful Git behavior assertion into a failed
        // test on Windows while Git releases a final file handle or object attribute.
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
