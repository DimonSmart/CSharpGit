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
            // Vulkan avoids the Win32 Skia resize repaint artifacts seen with the default backend; see IDD-0015.
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
