using Uno.UI.Hosting;

namespace CSharpGit.Presentation;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App? application = null;
        var host = UnoPlatformHostBuilder.Create()
            .App(() => application = new App())
            .UseWin32(builder => builder.RenderingBackend(Win32RenderingBackend.Vulkan))
            .UseMacOS()
            .UseX11()
            .UseLinuxFrameBuffer()
            .Build();

        try
        {
            host.RunAsync().GetAwaiter().GetResult();
        }
        finally
        {
            application?.StopHost();
        }
    }
}
