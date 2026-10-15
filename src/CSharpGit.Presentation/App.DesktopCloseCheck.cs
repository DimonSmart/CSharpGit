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

            if (Environment.GetEnvironmentVariable("CSHARPGIT_CLOSE_CHECK_EXTENDED") == "1")
                await RunDesktopLifecycleCommandsAsync(page, viewModel, reader, writer, timeout.Token);
            else if (await reader.ReadLineAsync(timeout.Token) != "Close")
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


    // Runs inside the real desktop UI, sharing the production ViewModel and MainPage.
    private async Task RunDesktopLifecycleCommandsAsync(
        MainPage page, OpenRepositoryViewModel vm,
        StreamReader reader, StreamWriter writer, CancellationToken token)
    {
        TaskCompletionSource? release = null;
        Task<bool>? mutation = null;
        Action? delayedMonitorDelivery = null;

        Task RespondAsync(bool success)
        {
            var repo = vm.Repository;
            var response = JsonSerializer.Serialize(new
            {
                State = "Snapshot",
                Success = success,
                Path = repo?.WorkingDirectory,
                IsWorktree = repo?.IsWorktree,
                Branch = vm.CurrentBranchName,
                Head = vm.CurrentHeadCommit,
                HasBaseline = vm.DisplayedWorkingTreeStatusSnapshot is not null,
                HistoryCount = vm.History.Rows.Count,
                Draft = vm.CommitCreation.CommitMessage,
                UnsafeToClose = page.IsUnsafeToClose,
                IsBusy = vm.IsBusy,
                RefreshRequired = page.IsRefreshRequired,
                Error = vm.ErrorMessage
            });
            return writer.WriteLineAsync(response.AsMemory(), token).AsTask();
        }

        while (true)
        {
            var command = await reader.ReadLineAsync(token)
                ?? throw new EndOfStreamException("Desktop lifecycle check disconnected.");
            if (command == "Close") return;

            if (command.StartsWith("Switch\t", StringComparison.Ordinal))
            {
                await RespondAsync(await vm.OpenRepositoryPathAsync(command["Switch\t".Length..], token));
            }
            else if (command.StartsWith("SetDraft\t", StringComparison.Ordinal))
            {
                vm.CommitCreation.CommitMessage = command["SetDraft\t".Length..];
                await RespondAsync(true);
            }
            else if (command == "CaptureMonitorDelivery")
            {
                delayedMonitorDelivery = page.CapturePendingMonitorDeliveryForDesktopCheck();
                await RespondAsync(true);
            }
            else if (command == "DeliverCapturedMonitor")
            {
                if (delayedMonitorDelivery is null)
                    throw new InvalidOperationException("No monitor callback was captured.");
                delayedMonitorDelivery();
                await RespondAsync(true);
            }
            else if (command == "CloseRepository")
            {
                await RespondAsync(vm.CloseRepository());
            }
            else if (command == "BeginMutation")
            {
                if (mutation is not null) throw new InvalidOperationException("Mutation already running.");
                release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var pendingRelease = release;
                mutation = vm.RunMutationAsync(async () =>
                {
                    started.TrySetResult();
                    await pendingRelease.Task;
                });
                await started.Task.WaitAsync(token);
                await RespondAsync(true);
            }
            else if (command == "AttemptCloseDuringMutation")
            {
                if (TryBeginNormalShutdown(page))
                    throw new InvalidOperationException("Close was accepted during a protected mutation.");
                await RespondAsync(true);
            }
            else if (command == "ThrowCompletedHandler")
            {
                Action<CSharpGit.Domain.Repository> thrower = _ =>
                    throw new InvalidOperationException("Lifecycle subscriber failure");
                vm.RepositoryStateRefreshCompleted += thrower;
                var reported = false;
                try
                {
                    await vm.RefreshAsyncForDesktopCheck();
                }
                catch (InvalidOperationException exception) when (
                    exception.Message == "Lifecycle subscriber failure")
                {
                    reported = true;
                }
                finally { vm.RepositoryStateRefreshCompleted -= thrower; }
                await RespondAsync(reported && !vm.IsBusy);
            }
            else if (command.StartsWith("AttemptSwitchDuringMutation\t", StringComparison.Ordinal))
            {
                var attempted = await vm.OpenRepositoryPathAsync(
                    command["AttemptSwitchDuringMutation\t".Length..], token);
                await RespondAsync(!attempted && vm.IsMutating);
            }
            else if (command == "AttemptCloseRepositoryDuringMutation")
            {
                await RespondAsync(!vm.CloseRepository() && vm.IsMutating);
            }
            else if (command == "AttemptSecondMutation")
            {
                var attempted = await vm.RunMutationAsync(
                    () => throw new InvalidOperationException("Rejected mutation must not execute."));
                await RespondAsync(!attempted && vm.IsMutating);
            }
            else if (command == "RetryMutation")
            {
                await RespondAsync(await vm.RunMutationAsync(() => Task.CompletedTask));
            }
            else if (command == "CompleteMutation")
            {
                if (mutation is null || release is null)
                    throw new InvalidOperationException("No mutation is running.");
                release.TrySetResult();
                var result = await mutation.WaitAsync(token);
                mutation = null;
                release = null;
                await RespondAsync(result);
            }
            else throw new InvalidOperationException($"Unknown desktop lifecycle command: {command}");
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
