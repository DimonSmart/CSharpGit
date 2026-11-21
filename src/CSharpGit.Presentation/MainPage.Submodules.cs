using System.Diagnostics;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private readonly ISubmoduleService _submoduleService = null!;

    private async Task<(Repository Repository, SubmoduleInfo? Info)?> FindSubmoduleAsync(
        RepositorySnapshotEntry entry)
    {
        var repository = _repositoryFilesViewModel.CurrentRepository;
        if (repository is null || !ReferenceEquals(repository, _viewModel.Repository)) return null;
        var infos = await _submoduleService.ReadAsync(repository);
        if (IsShuttingDown || !ReferenceEquals(repository, _viewModel.Repository)) return null;
        return (repository, infos.FirstOrDefault(info =>
            string.Equals(info.Path, entry.Path, StringComparison.Ordinal)));
    }

    private async Task OpenSelectedSubmoduleAsync(RepositorySnapshotEntry entry)
    {
        try
        {
            var selected = await FindSubmoduleAsync(entry);
            if (selected is not { } item) return;
            if (item.Info is not { CanOpen: true } info)
            {
                await ShowSubmoduleInfoAsync(entry, item.Info);
                return;
            }

            var folder = Path.GetFullPath(Path.Combine(item.Repository.WorkingDirectory,
                info.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!Directory.Exists(folder))
                throw new InvalidOperationException("Submodule directory is unavailable.");
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Application executable path is unavailable.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet",
                    StringComparison.OrdinalIgnoreCase))
            {
                var assembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(assembly))
                    throw new InvalidOperationException("Application entry assembly is unavailable.");
                start.ArgumentList.Add(assembly);
            }
            start.ArgumentList.Add(folder);
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start another CSharpGit process.");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { await ShowErrorAsync("Could not open submodule", exception.Message); }
    }

    private async Task ShowSubmoduleMenuAsync(FrameworkElement target, RepositorySnapshotEntry entry)
    {
        try
        {
            var selected = await FindSubmoduleAsync(entry);
            if (selected is not { } item) return;
            var repository = item.Repository;
            var info = item.Info;
            var flyout = new MenuFlyout();
            AddMenuItem(flyout, "Information", true, () => ShowSubmoduleInfoAsync(entry, info));
            flyout.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem(flyout, "Open Repository", info is { CanOpen: true },
                () => OpenSelectedSubmoduleAsync(entry));
            AddMenuItem(flyout, "Initialize",
                info is { IsInitialized: false, IsConflicted: false, ErrorMessage: null },
                () => RunSubmoduleOperationAsync(repository, "Initialize submodule",
                    token => _submoduleService.InitializeAsync(repository, entry.Path, token)));
            AddMenuItem(flyout, "Update to Recorded Commit",
                info is { IsInitialized: true, IsConflicted: false, ErrorMessage: null },
                () => RunSubmoduleOperationAsync(repository, "Update submodule",
                    token => _submoduleService.UpdateAsync(repository, entry.Path, token)));
            AddMenuItem(flyout, "Update All Submodules", true,
                () => RunSubmoduleOperationAsync(repository, "Update all submodules",
                    token => _submoduleService.UpdateAllAsync(repository, token)));
            flyout.Items.Add(new MenuFlyoutSeparator());
            if (!string.IsNullOrWhiteSpace(info?.Url))
                AddMenuItem(flyout, "Copy URL", true, () => CopyTextAsync(info.Url!));
            AddMenuItem(flyout, "Copy Recorded Commit Hash", true,
                () => CopyTextAsync(info?.RecordedCommit ?? entry.ObjectId));
            if (!string.Equals(info?.RecordedCommit, entry.ObjectId, StringComparison.Ordinal))
                AddMenuItem(flyout, "Copy Historical Commit Hash", true,
                    () => CopyTextAsync(entry.ObjectId));
            flyout.ShowAt(target);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { await ShowErrorAsync("Could not read submodule", exception.Message); }
    }

    private async Task ShowSubmoduleInfoAsync(RepositorySnapshotEntry entry, SubmoduleInfo? info)
    {
        var details = string.Join(Environment.NewLine, new[]
        {
            $"Path: {entry.Path}",
            $"URL from .gitmodules: {info?.Url ?? "unavailable"}",
            $"Historical snapshot commit: {entry.ObjectId}",
            $"Parent index commit: {info?.RecordedCommit ?? "not present"}",
            $"Current checkout commit: {info?.CurrentCommit ?? "not initialized"}",
            $"Branch: {info?.Branch ?? (info?.IsInitialized == true ? "detached HEAD" : "—")}",
            $"Status: {info?.StatusText ?? "Not present in current index"}",
            $"Modified internal content: {(info?.ModifiedContent == true ? "yes" : "no")}",
            $"Untracked internal content: {(info?.UntrackedContent == true ? "yes" : "no")}"
        });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Submodule",
            Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Close"
        };
        await ShowPageDialogAsync(dialog);
    }

    private async Task RunSubmoduleOperationAsync(
        Repository repository, string title, Func<CancellationToken, Task> mutation)
    {
        if (IsShuttingDown || !ReferenceEquals(repository, _viewModel.Repository)) return;
        using var lease = _viewModel.TryBeginUserGitOperation(repository);
        if (lease is null) return;
        using var cancellation = new CancellationTokenSource();
        var ring = new ProgressRing { Width = 24, Height = 24, IsActive = true };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "Git operation in progress…", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(ring);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = "Cancel"
        };
        var started = false;
        var finished = false;
        var succeeded = false;
        string? failure = null;
        dialog.Loaded += (_, _) =>
        {
            if (started) return;
            started = true;
            RunPageUiOperation(async () =>
            {
                try { await mutation(cancellation.Token); succeeded = true; }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception exception) { failure = exception.Message; }
                finally
                {
                    finished = true;
                    ring.IsActive = false;
                    dialog.Hide();
                }
            }, title);
        };
        dialog.Closing += (_, args) =>
        {
            if (finished) return;
            cancellation.Cancel();
            args.Cancel = true;
        };
        await ShowPageDialogAsync(dialog);
        lease.Dispose();
        if (IsShuttingDown || !ReferenceEquals(repository, _viewModel.Repository)) return;
        if (succeeded)
        {
            await _viewModel.RefreshAsyncForDesktopCheck();
            await _repositoryFilesViewModel.RefreshAsync();
        }
        else if (failure is not null)
            await ShowErrorAsync(title + " failed", failure);
    }
}
