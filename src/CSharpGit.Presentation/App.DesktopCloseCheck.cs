using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using CSharpGit.Infrastructure;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Uno.UI.NativeElementHosting;
using Uno.UI.Xaml;

namespace CSharpGit.Presentation;

public sealed partial class App
{
    private async void RunDesktopCloseCheckWhenLoaded(object sender, RoutedEventArgs args)
    {
        if (_closeCheckStarted) return;
        _closeCheckStarted = true;
        var page = (MainPage)sender;
        page.Loaded -= RunDesktopCloseCheckWhenLoaded;

        try
        {
            var pipeName = Environment.GetEnvironmentVariable("CSHARPGIT_CLOSE_CHECK_PIPE");
            if (string.IsNullOrWhiteSpace(pipeName))
                throw new InvalidOperationException("The desktop close-check pipe was not configured.");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            ReportDesktopCloseCheck("RepositoryOpening");
            var repository = Environment.GetEnvironmentVariable("CSHARPGIT_UI_CHECK_REPOSITORY");
            if (string.IsNullOrWhiteSpace(repository)
                || page.DataContext is not OpenRepositoryViewModel viewModel)
                throw new InvalidOperationException("The desktop close-check repository was not configured.");
            if (!await viewModel.OpenRepositoryPathAsync(repository, timeout.Token))
                throw new InvalidOperationException($"The desktop close-check repository did not open: {viewModel.ErrorMessage}");
            if (page.IsUnsafeToClose || page.RequiresCloseConfirmation)
                throw new InvalidOperationException("The desktop close-check repository is not ready for normal closure.");

            ReportDesktopCloseCheck("Ready");
            await writer.WriteLineAsync(JsonSerializer.Serialize(new
            {
                State = "Ready",
                Repository = viewModel.Repository!.RepositoryRoot,
                ApplicationDataRoot = ApplicationDataPaths.Root
            }).AsMemory(), timeout.Token);

            if (await reader.ReadLineAsync(timeout.Token) != "Close")
                throw new InvalidOperationException("The desktop close-check pipe did not receive a Close command.");

            ReportDesktopCloseCheck("CloseRequested");
            if (!_window!.DispatcherQueue.TryEnqueue(() =>
                {
                    ReportDesktopCloseCheck("CloseDispatched");
                    RequestNormalWindowCloseForDesktopCheck();
                }))
                throw new InvalidOperationException("The UI dispatcher rejected the desktop close request.");
        }
        catch (Exception exception)
        {
            ReportDesktopCloseCheck($"Failed: {exception}");
        }
    }

    private void RequestNormalWindowCloseForDesktopCheck()
    {
        if (OperatingSystem.IsWindows())
        {
            // Uno's Window.Close destroys the Win32 window without raising AppWindow.Closing.
            // WM_CLOSE follows the same cancelable path as the title-bar close button.
            if (_window!.GetNativeWindow() is not Win32NativeWindow nativeWindow)
                throw new InvalidOperationException("The desktop window does not have a native Win32 handle.");
            var handle = nativeWindow.Hwnd;
            if (!PostMessageW(handle, 0x0010, 0, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        else if (TryBeginNormalShutdown(_window!.Content as MainPage))
        {
            _window.Close();
        }
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    private static void ReportDesktopCloseCheck(string phase)
    {
        if (Environment.GetEnvironmentVariable("CSHARPGIT_CLOSE_CHECK") == "1")
            Console.WriteLine($"DesktopCloseCheck: {phase}");
    }
}
