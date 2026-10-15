namespace CSharpGit.Infrastructure;

public static class ApplicationDataPaths
{
    public const string RootEnvironmentVariable = "CSHARPGIT_APP_DATA_ROOT";

    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
                return Path.GetFullPath(configured);

            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrWhiteSpace(root) ? AppContext.BaseDirectory : root;
        }
    }
}
