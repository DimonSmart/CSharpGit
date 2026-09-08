using Uno.UI.Hosting;

namespace CSharpGit.Presentation;

public static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseWin32()
            .UseMacOS()
            .UseX11()
            .UseLinuxFrameBuffer()
            .Build();

        await host.RunAsync();
    }
}
