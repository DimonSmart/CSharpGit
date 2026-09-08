namespace CSharpGit.Application.Tests;

public sealed class NativeFolderPickerContractTests
{
    [Fact]
    public void NativePickerIsInitializedWithTheMainWindowHandle()
    {
        var root = FindRepositoryRoot();
        var picker = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Infrastructure", "NativeFolderPicker.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "App.xaml.cs"));

        Assert.Contains("InitializeWithWindow.Initialize(picker, _ownerWindowHandle())", picker);
        Assert.Contains("WindowNative.GetWindowHandle(_window)", app);
    }

    [Fact]
    public void DesktopEntryPointKeepsWin32MessageLoopInStaApartment()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Platforms", "Desktop", "Program.cs"));

        Assert.Contains("[STAThread]", program);
        Assert.Contains("public static void Main(string[] args)", program);
        Assert.DoesNotContain("async Task Main", program);
        Assert.Contains("host.RunAsync().GetAwaiter().GetResult();", program);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
