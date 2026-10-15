using System.ComponentModel;
using System.Diagnostics;
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

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(
                Environment.GetEnvironmentVariable("CSHARPGIT_CLOSE_CHECK_EXTENDED") == "1" ? 180 : 45));
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
        var publicationFaults = new List<PropertyChangedEventHandler>();

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
                BranchCount = vm.Branches.LocalBranches.Count,
                TagCount = vm.Tags.Items.Count,
                StashCount = vm.Stashes.Items.Count,
                Draft = vm.CommitCreation.CommitMessage,
                UnsafeToClose = page.IsUnsafeToClose,
                IsBusy = vm.IsBusy,
                CanChangeRepository = vm.CanChangeRepository,
                RefreshRequired = page.IsRefreshRequired,
                Error = vm.ErrorMessage
            });
            return writer.WriteLineAsync(response.AsMemory(), token).AsTask();
        }

        while (true)
        {
            var command = await reader.ReadLineAsync(token)
                ?? throw new EndOfStreamException("Desktop lifecycle check disconnected.");
            if (command == "Close")
            {
                foreach (var handler in publicationFaults) vm.PropertyChanged -= handler;
                return;
            }

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
            else if (command.StartsWith("FailNextPublication\t", StringComparison.Ordinal))
            {
                var parts = command.Split('\t', 3);
                if (parts.Length != 3) throw new InvalidOperationException("Expected repository path and failure message.");
                var path = Path.GetFullPath(parts[1]);
                var message = parts[2];
                PropertyChangedEventHandler handler = null!;
                handler = (_, args) =>
                {
                    if (args.PropertyName != nameof(OpenRepositoryViewModel.CurrentBranchName)
                        || string.IsNullOrWhiteSpace(vm.CurrentBranchName)
                        || !string.Equals(Path.GetFullPath(vm.Repository?.WorkingDirectory ?? ""),
                            path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        return;
                    vm.PropertyChanged -= handler;
                    publicationFaults.Remove(handler);
                    throw new InvalidOperationException(message);
                };
                publicationFaults.Add(handler);
                vm.PropertyChanged += handler;
                await RespondAsync(true);
            }
            else if (command == "CommitWithFailedRefresh")
            {
                var repository = vm.Repository ?? throw new InvalidOperationException("No selected repository.");
                PropertyChangedEventHandler handler = null!;
                handler = (_, args) =>
                {
                    if (args.PropertyName != nameof(OpenRepositoryViewModel.CurrentHeadCommit)) return;
                    vm.PropertyChanged -= handler;
                    publicationFaults.Remove(handler);
                    throw new InvalidOperationException("Injected post-commit refresh failure");
                };
                publicationFaults.Add(handler);
                vm.PropertyChanged += handler;
                var result = await vm.RunMutationAsync(async () =>
                {
                    var start = new ProcessStartInfo("git")
                    {
                        WorkingDirectory = repository.WorkingDirectory,
                        UseShellExecute = false,
                        RedirectStandardError = true
                    };
                    foreach (var argument in new[] {
                        "-c", "commit.gpgsign=false",
                        "-c", "core.hooksPath=/nonexistent-csharpgit-hooks",
                        "commit", "--allow-empty", "-m", "Lifecycle integration test commit"
                    }) start.ArgumentList.Add(argument);
                    using var process = Process.Start(start)
                        ?? throw new InvalidOperationException("Git commit did not start.");
                    var stderr = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync(token);
                    if (process.ExitCode != 0) throw new IOException($"Git commit failed: {stderr}");
                });
                await RespondAsync(result);
            }
            else if (command == "Refresh")
            {
                await vm.RefreshAsyncForDesktopCheck();
                await RespondAsync(true);
            }
            else if (command.StartsWith("ReadFailureAfterAssignment\t", StringComparison.Ordinal))
            {
                var target = command["ReadFailureAfterAssignment\t".Length..];
                var pointerPath = Path.Combine(target, ".git");
                var originalPointer = File.ReadAllText(pointerPath);
                var corruptPointer = $"gitdir: {Path.Combine(target, "missing-gitdir-for-lifecycle-test")}\n";
                PropertyChangedEventHandler handler = null!;
                var injected = false;
                handler = (_, args) =>
                {
                    if (args.PropertyName != nameof(OpenRepositoryViewModel.Repository)
                        || !string.Equals(vm.Repository?.WorkingDirectory, target,
                            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        return;
                    vm.PropertyChanged -= handler;
                    File.WriteAllText(pointerPath, corruptPointer);
                    injected = true;
                };
                vm.PropertyChanged += handler;
                bool opened;
                try { opened = await vm.OpenRepositoryPathAsync(target, token); }
                finally
                {
                    vm.PropertyChanged -= handler;
                    File.WriteAllText(pointerPath, originalPointer);
                }
                await RespondAsync(!opened && injected);
            }
            else if (command.StartsWith("CancelOpenBeforeAssignment\t", StringComparison.Ordinal))
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                var cancelled = false;
                try { await vm.OpenRepositoryPathAsync(
                    command["CancelOpenBeforeAssignment\t".Length..], cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                await RespondAsync(cancelled);
            }
            else if (command.StartsWith("CancelOpenAfterAssignment\t", StringComparison.Ordinal))
            {
                var target = command["CancelOpenAfterAssignment\t".Length..];
                using var cancel = new CancellationTokenSource();
                PropertyChangedEventHandler handler = null!;
                handler = (_, args) =>
                {
                    if (args.PropertyName != nameof(OpenRepositoryViewModel.Repository)
                        || !string.Equals(vm.Repository?.WorkingDirectory, target,
                            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        return;
                    vm.PropertyChanged -= handler;
                    cancel.Cancel();
                };
                vm.PropertyChanged += handler;
                var cancelled = false;
                try { await vm.OpenRepositoryPathAsync(target, cancel.Token); }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { cancelled = true; }
                finally { vm.PropertyChanged -= handler; }
                await RespondAsync(cancelled);
            }
            else if (command == "FailBeforeMutation")
            {
                var executed = 0;
                var result = await vm.RunMutationAsync(
                    () => { executed++; return Task.CompletedTask; },
                    beforeMutation: () => throw new IOException("Injected pre-mutation failure"));
                await RespondAsync(!result && executed == 0 && vm.CanChangeRepository);
            }
            else if (command == "FailAfterSuccessfulMutation")
            {
                var executed = 0;
                var result = await vm.RunMutationAsync(
                    () => { executed++; return Task.CompletedTask; },
                    afterSuccessfulMutation: () =>
                        throw new IOException("Injected post-mutation hook failure"));
                await RespondAsync(!result && executed == 1 && vm.CanChangeRepository);
            }
            else if (command == "FailMutation")
            {
                await RespondAsync(await vm.RunMutationAsync(() =>
                    throw new IOException("Injected Git mutation failure")));
            }
            else if (command == "CancelMutation")
            {
                var cancelled = false;
                try
                {
                    await vm.RunMutationAsync(() =>
                        throw new OperationCanceledException("Injected Git mutation cancellation"));
                }
                catch (OperationCanceledException) { cancelled = true; }
                await RespondAsync(cancelled);
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
