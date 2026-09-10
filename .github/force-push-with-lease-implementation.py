from pathlib import Path

ROOT = Path.cwd()


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content, encoding="utf-8")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one occurrence, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


# Domain: immutable confirmation/execution snapshot.
write("src/CSharpGit.Domain/ForcePushWithLease.cs", r'''namespace CSharpGit.Domain;

public sealed record ForcePushWithLeaseSnapshot(
    string LocalBranch,
    string LocalCommit,
    string Remote,
    string RemotePushDestination,
    string RemoteBranch,
    string ExpectedRemoteCommit,
    string? LocalCommitSubject = null,
    string? RemoteCommitSubject = null);
''')

# Application contract: ordinary Push remains separate; no force flag/mode exists.
interface_path = "src/CSharpGit.Application/Abstractions/IRepositoryStateService.cs"
replace_once(
    interface_path,
    "    Task PushAsync(Repository repository, string? remote = null, string? branch = null, bool setUpstream = false, CancellationToken cancellationToken = default);\n",
    "    Task PushAsync(Repository repository, string? remote = null, string? branch = null, bool setUpstream = false, CancellationToken cancellationToken = default);\n"
    "    Task<ForcePushWithLeaseSnapshot> PrepareForcePushWithLeaseAsync(Repository repository, string? remote = null, string? remoteBranch = null, CancellationToken cancellationToken = default);\n"
    "    Task ForcePushWithLeaseAsync(Repository repository, ForcePushWithLeaseSnapshot snapshot, CancellationToken cancellationToken = default);\n")

# Classified push failures keep RepositoryOpenException compatibility for callers that catch the base type.
replace_once(
    "src/CSharpGit.Application/Exceptions/RepositoryOpenException.cs",
    "public sealed class RepositoryOpenException(string message, Exception? innerException = null)",
    "public class RepositoryOpenException(string message, Exception? innerException = null)")

write("src/CSharpGit.Application/Exceptions/PushRejectedException.cs", r'''namespace CSharpGit.Application.Exceptions;

public enum PushResultKind
{
    Success,
    NonFastForwardRejected,
    LeaseRejected,
    RemoteRejected,
    AuthenticationOrTransportFailure,
    OtherFailure
}

public sealed class PushRejectedException(
    PushResultKind resultKind,
    string message,
    Exception? innerException = null)
    : RepositoryOpenException(message, innerException)
{
    public PushResultKind ResultKind { get; } = resultKind;
}

public enum ForcePushPreparationFailure
{
    MissingUpstream,
    RemoteBranchDoesNotExist,
    MultiplePushDestinations
}

public sealed class ForcePushWithLeasePreparationException(
    ForcePushPreparationFailure failure,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public ForcePushPreparationFailure Failure { get; } = failure;
}

public sealed class ForcePushWithLeaseCancelledException(string message)
    : Exception(message);
''')

# Ordinary push gains machine-readable failure classification but no force semantics.
git_service_path = "src/CSharpGit.Git/GitCliRepositoryService.cs"
old_push = r'''    public async Task PushAsync(Repository repository, string? remote = null, string? branch = null, bool setUpstream = false, CancellationToken cancellationToken = default)
    {
        var current = await CurrentBranchAsync(repository, cancellationToken);
        var upstream = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}");
        if (!string.IsNullOrWhiteSpace(upstream) && remote is null && branch is null)
        {
            await RunGitForMutationAsync(repository, cancellationToken, "push");
            return;
        }
        if (string.IsNullOrWhiteSpace(remote) || string.IsNullOrWhiteSpace(branch))
            throw new InvalidOperationException("No upstream is configured. Explicitly select a remote and remote branch name; Git GUI does not select them automatically.");
        ValidateRefName(remote, nameof(remote));
        ValidateRefName(branch, nameof(branch));
        var refspec = $"{current}:refs/heads/{branch}";
        await RunGitForMutationAsync(repository, cancellationToken, setUpstream ? ["push", "--set-upstream", remote, refspec] : ["push", remote, refspec]);
    }
'''
new_push = r'''    public async Task PushAsync(Repository repository, string? remote = null, string? branch = null, bool setUpstream = false, CancellationToken cancellationToken = default)
    {
        var current = await CurrentBranchAsync(repository, cancellationToken);
        var upstream = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}");
        if (!string.IsNullOrWhiteSpace(upstream) && remote is null && branch is null)
        {
            await RunPushAsync(repository, cancellationToken, "push", "--porcelain");
            return;
        }
        if (string.IsNullOrWhiteSpace(remote) || string.IsNullOrWhiteSpace(branch))
            throw new InvalidOperationException("No upstream is configured. Explicitly select a remote and remote branch name; Git GUI does not select them automatically.");
        ValidateRefName(remote, nameof(remote));
        ValidateRefName(branch, nameof(branch));
        var refspec = $"{current}:refs/heads/{branch}";
        await RunPushAsync(repository, cancellationToken,
            setUpstream
                ? ["push", "--porcelain", "--set-upstream", remote, refspec]
                : ["push", "--porcelain", remote, refspec]);
    }
'''
replace_once(git_service_path, old_push, new_push)

write("src/CSharpGit.Git/GitCliRepositoryService.ForcePush.cs", r'''using System.Diagnostics;
using System.Text;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git;

public sealed partial class GitCliRepositoryService
{
    public async Task<ForcePushWithLeaseSnapshot> PrepareForcePushWithLeaseAsync(
        Repository repository,
        string? remote = null,
        string? remoteBranch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (DetectOperation(repository) != RepositoryOperation.None)
            throw new InvalidOperationException("Complete or abort the current Git operation before force pushing.");
        if ((remote is null) != (remoteBranch is null))
            throw new ArgumentException("Explicit force-push target requires both remote and remote branch.");

        var localBranch = await CurrentBranchAsync(repository, cancellationToken);
        ValidateRefName(localBranch, nameof(localBranch));
        var localRef = $"refs/heads/{localBranch}";
        var localCommit = await RunGitAsync(repository.WorkingDirectory, cancellationToken, true,
            "rev-parse", "--verify", localRef);
        ValidateObjectName(localCommit);

        if (remote is null)
        {
            remote = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken,
                "config", "--get", $"branch.{localBranch}.remote");
            var mergeRef = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken,
                "config", "--get", $"branch.{localBranch}.merge");
            if (string.IsNullOrWhiteSpace(remote) ||
                string.IsNullOrWhiteSpace(mergeRef) ||
                !mergeRef.StartsWith("refs/heads/", StringComparison.Ordinal) ||
                mergeRef.Length <= "refs/heads/".Length)
            {
                throw new ForcePushWithLeasePreparationException(
                    ForcePushPreparationFailure.MissingUpstream,
                    $"Branch '{localBranch}' has no unambiguous configured upstream. Explicitly select a remote and remote branch.");
            }
            remoteBranch = mergeRef["refs/heads/".Length..];
        }

        remote = remote.Trim();
        remoteBranch = remoteBranch!.Trim();
        ValidateRefName(remote, nameof(remote));
        ValidateRefName(remoteBranch, nameof(remoteBranch));
        await EnsureRemoteExistsAsync(repository, remote, cancellationToken);
        var pushDestination = await ReadSinglePushDestinationAsync(repository, remote, cancellationToken);
        var remoteRef = $"refs/heads/{remoteBranch}";
        var remoteQuery = await RunGitCapturedAsync(repository, cancellationToken,
            "ls-remote", "--exit-code", "--refs", pushDestination, remoteRef);
        if (remoteQuery.ExitCode == 2)
        {
            throw new ForcePushWithLeasePreparationException(
                ForcePushPreparationFailure.RemoteBranchDoesNotExist,
                $"{remote}/{remoteBranch} does not currently exist. Use normal Push to create a new remote branch.");
        }
        if (remoteQuery.ExitCode != 0)
            throw CreateGitFailure("Could not query the force-push destination", remoteQuery);

        var matchingLines = remoteQuery.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length == 2 && string.Equals(fields[1], remoteRef, StringComparison.Ordinal))
            .ToList();
        if (matchingLines.Count != 1)
            throw new RepositoryOpenException("Git ls-remote did not return exactly one matching remote branch ref.");
        var expectedRemoteCommit = matchingLines[0][0];
        ValidateObjectName(expectedRemoteCommit);

        var localSubject = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false,
            "show", "-s", "--format=%s", localCommit);
        string? remoteSubject = null;
        var objectCheck = await RunGitCapturedAsync(repository, cancellationToken,
            "cat-file", "-e", $"{expectedRemoteCommit}^{{commit}}");
        if (objectCheck.ExitCode == 0)
        {
            var subject = await RunOptionalGitAsync(repository.WorkingDirectory, cancellationToken,
                "show", "-s", "--format=%s", expectedRemoteCommit);
            remoteSubject = string.IsNullOrWhiteSpace(subject) ? null : subject;
        }

        return new ForcePushWithLeaseSnapshot(
            localBranch,
            localCommit,
            remote,
            pushDestination,
            remoteBranch,
            expectedRemoteCommit,
            string.IsNullOrWhiteSpace(localSubject) ? null : localSubject,
            remoteSubject);
    }

    public async Task ForcePushWithLeaseAsync(
        Repository repository,
        ForcePushWithLeaseSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        if (DetectOperation(repository) != RepositoryOperation.None)
            throw new ForcePushWithLeaseCancelledException("A Git operation started after confirmation was prepared. Start the force-push workflow again.");

        var currentBranch = await CurrentBranchAsync(repository, cancellationToken);
        if (!string.Equals(currentBranch, snapshot.LocalBranch, StringComparison.Ordinal))
            throw new ForcePushWithLeaseCancelledException("The current branch changed after confirmation was prepared. Start the operation again.");

        var currentLocalCommit = await RunGitAsync(repository.WorkingDirectory, cancellationToken, true,
            "rev-parse", "--verify", $"refs/heads/{snapshot.LocalBranch}");
        if (!string.Equals(currentLocalCommit, snapshot.LocalCommit, StringComparison.Ordinal))
            throw new ForcePushWithLeaseCancelledException($"{snapshot.LocalBranch} changed after confirmation was prepared. Review the new state and start the operation again.");

        string currentPushDestination;
        try
        {
            currentPushDestination = await ReadSinglePushDestinationAsync(repository, snapshot.Remote, cancellationToken);
        }
        catch (ForcePushWithLeasePreparationException exception)
        {
            throw new ForcePushWithLeaseCancelledException($"The push destination changed after confirmation was prepared: {exception.Message}");
        }
        if (!string.Equals(currentPushDestination, snapshot.RemotePushDestination, StringComparison.Ordinal))
            throw new ForcePushWithLeaseCancelledException("The remote push destination changed after confirmation was prepared. Start the operation again.");

        // Do not query the remote OID here. The exact OID in the confirmed immutable
        // snapshot is the lease and Git itself performs the compare-and-swap check.
        await RunPushAsync(repository, cancellationToken, BuildForcePushArguments(snapshot));
    }

    internal static string[] BuildForcePushArguments(ForcePushWithLeaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        return
        [
            "push",
            "--porcelain",
            $"--force-with-lease=refs/heads/{snapshot.RemoteBranch}:{snapshot.ExpectedRemoteCommit}",
            snapshot.Remote,
            $"refs/heads/{snapshot.LocalBranch}:refs/heads/{snapshot.RemoteBranch}"
        ];
    }

    private static void ValidateSnapshot(ForcePushWithLeaseSnapshot snapshot)
    {
        ValidateRefName(snapshot.LocalBranch, nameof(snapshot.LocalBranch));
        ValidateRefName(snapshot.Remote, nameof(snapshot.Remote));
        ValidateRefName(snapshot.RemoteBranch, nameof(snapshot.RemoteBranch));
        ValidateObjectName(snapshot.LocalCommit);
        ValidateObjectName(snapshot.ExpectedRemoteCommit);
        if (string.IsNullOrWhiteSpace(snapshot.RemotePushDestination))
            throw new ArgumentException("The force-push snapshot has no push destination.", nameof(snapshot));
    }

    private async Task EnsureRemoteExistsAsync(Repository repository, string remote, CancellationToken cancellationToken)
    {
        var remotes = await RunGitAsync(repository.WorkingDirectory, cancellationToken, false, "remote");
        if (!remotes.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(remote, StringComparer.Ordinal))
            throw new ArgumentException($"Remote '{remote}' is not configured.", nameof(remote));
    }

    private async Task<string> ReadSinglePushDestinationAsync(Repository repository, string remote, CancellationToken cancellationToken)
    {
        ValidateRefName(remote, nameof(remote));
        await EnsureRemoteExistsAsync(repository, remote, cancellationToken);
        var result = await RunGitCapturedAsync(repository, cancellationToken,
            "remote", "get-url", "--push", "--all", remote);
        if (result.ExitCode != 0)
            throw CreateGitFailure($"Could not resolve push URL for remote '{remote}'", result);
        var destinations = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (destinations.Length == 0)
            throw new RepositoryOpenException($"Remote '{remote}' has no push destination.");
        if (destinations.Length != 1)
            throw new ForcePushWithLeasePreparationException(
                ForcePushPreparationFailure.MultiplePushDestinations,
                "Force push with lease is not available for remotes with multiple push destinations.");
        return destinations[0];
    }

    private async Task RunPushAsync(Repository repository, CancellationToken cancellationToken, params string[] arguments)
    {
        var result = await RunGitCapturedAsync(repository, cancellationToken, arguments);
        if (result.ExitCode == 0) return;
        var detail = JoinGitOutput(result);
        throw new PushRejectedException(
            ClassifyPushFailure(detail),
            $"Git push failed: {(string.IsNullOrWhiteSpace(detail) ? $"exit code {result.ExitCode}" : detail)}");
    }

    internal static PushResultKind ClassifyPushFailure(string output)
    {
        var text = output.ToLowerInvariant();

        // Authentication/transport signals have priority. Git commonly prefixes
        // diagnostics with "remote:", which must not turn auth failures into a
        // generic remote-policy rejection.
        if (ContainsAny(text,
                "authentication failed", "could not read username", "permission denied",
                "publickey", "repository not found", "unable to access", "could not resolve host",
                "failed to connect", "connection timed out", "connection reset", "network is unreachable",
                "remote end hung up", "connection closed"))
            return PushResultKind.AuthenticationOrTransportFailure;

        if (ContainsAny(text, "stale info", "force-with-lease"))
            return PushResultKind.LeaseRejected;

        if (ContainsAny(text, "non-fast-forward", "fetch first"))
            return PushResultKind.NonFastForwardRejected;

        if (ContainsAny(text,
                "remote rejected", "[remote rejected]", "pre-receive hook declined",
                "hook declined", "protected branch", "deny updating"))
            return PushResultKind.RemoteRejected;

        return PushResultKind.OtherFailure;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.Ordinal));

    private static RepositoryOpenException CreateGitFailure(string prefix, GitCommandResult result)
    {
        var detail = JoinGitOutput(result);
        return new RepositoryOpenException(
            $"{prefix}: {(string.IsNullOrWhiteSpace(detail) ? $"Git exited with code {result.ExitCode}." : detail)}");
    }

    private static string JoinGitOutput(GitCommandResult result)
    {
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return result.StandardError.Trim();
        if (string.IsNullOrWhiteSpace(result.StandardError)) return result.StandardOutput.Trim();
        return $"{result.StandardOutput.Trim()}\n{result.StandardError.Trim()}";
    }

    private async Task<GitCommandResult> RunGitCapturedAsync(
        Repository repository,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(_gitExecutable)
        {
            WorkingDirectory = repository.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RepositoryOpenException($"Git executable '{_gitExecutable}' could not be started.", exception);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
            }
            throw;
        }
        return new GitCommandResult(process.ExitCode, await stdout, await stderr);
    }

    private sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
}
''')

# Page-level workflow keeps the confirmed snapshot local and passes that exact object to Execute.
write("src/CSharpGit.Presentation/MainPage.ForcePush.cs", r'''using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private async Task PushFromUiAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;
        var repository = _viewModel.Repository;
        var hasExplicitBranch = !string.IsNullOrWhiteSpace(_viewModel.PushBranchName);
        var remote = hasExplicitBranch ? _viewModel.SelectedRemote?.Name : null;
        var branch = hasExplicitBranch ? _viewModel.PushBranchName.Trim() : null;
        if (hasExplicitBranch && string.IsNullOrWhiteSpace(remote))
        {
            await ShowErrorAsync("Push target required", "Select a remote for the explicit push target.");
            return;
        }

        try
        {
            await _referenceService.PushAsync(repository, remote, branch, hasExplicitBranch && _viewModel.SetUpstream);
            await RefreshAfterRemoteOperationAsync();
        }
        catch (PushRejectedException exception) when (exception.ResultKind == PushResultKind.NonFastForwardRejected)
        {
            await RefreshAfterRemoteOperationAsync();
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Push rejected",
                Content = "The remote history differs from your local history. This can happen after rebase or amend.",
                PrimaryButtonText = "Force push with lease…",
                SecondaryButtonText = "Fetch",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                await _referenceService.FetchAllAsync(repository);
                await RefreshAfterRemoteOperationAsync();
            }
            else if (result == ContentDialogResult.Primary)
            {
                await RunForcePushWithLeaseAsync();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RefreshAfterRemoteOperationAsync();
            await ShowErrorAsync("Push failed", exception.Message);
        }
    }

    private async Task RunForcePushWithLeaseAsync()
    {
        if (_viewModel.Repository is null || _viewModel.IsBusy) return;
        if (_viewModel.CurrentOperation != RepositoryOperation.None)
        {
            await ShowErrorAsync("Force push with lease unavailable", "Complete or abort the current Git operation first.");
            return;
        }

        var repository = _viewModel.Repository;
        ForcePushWithLeaseSnapshot snapshot;
        try
        {
            snapshot = await _referenceService.PrepareForcePushWithLeaseAsync(repository);
        }
        catch (ForcePushWithLeasePreparationException exception)
            when (exception.Failure == ForcePushPreparationFailure.MissingUpstream)
        {
            var explicitRemote = _viewModel.SelectedRemote?.Name;
            var explicitBranch = _viewModel.PushBranchName.Trim();
            if (string.IsNullOrWhiteSpace(explicitRemote) || string.IsNullOrWhiteSpace(explicitBranch))
            {
                await ShowErrorAsync(
                    "Explicit push target required",
                    "This branch has no configured upstream. Select Remote and Remote branch name in Git operations, then choose Force push with lease again.");
                await GitOperationsDialog.ShowAsync();
                return;
            }
            try
            {
                snapshot = await _referenceService.PrepareForcePushWithLeaseAsync(repository, explicitRemote, explicitBranch);
            }
            catch (Exception explicitException) when (explicitException is not OperationCanceledException)
            {
                await ShowForcePreparationFailureAsync(explicitException);
                return;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowForcePreparationFailureAsync(exception);
            return;
        }

        var confirmation = new StackPanel { Spacing = 8, Width = 540 };
        confirmation.Children.Add(new TextBlock { Text = "Rewrite remote branch history?", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        confirmation.Children.Add(new TextBlock { Text = $"Local branch: {snapshot.LocalBranch}" });
        confirmation.Children.Add(new TextBlock { Text = $"Remote branch: {snapshot.Remote}/{snapshot.RemoteBranch}" });
        confirmation.Children.Add(new TextBlock { Text = $"Local: {ShortOid(snapshot.LocalCommit)}{FormatSubject(snapshot.LocalCommitSubject)}" });
        confirmation.Children.Add(new TextBlock { Text = $"Remote: {ShortOid(snapshot.ExpectedRemoteCommit)}{FormatSubject(snapshot.RemoteCommitSubject)}" });
        confirmation.Children.Add(new TextBlock
        {
            Text = "Remote history may be replaced by your local history.",
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Force push with lease",
            Content = confirmation,
            PrimaryButtonText = "Force push with lease",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            // Snapshot is intentionally the exact immutable object shown above.
            await _referenceService.ForcePushWithLeaseAsync(repository, snapshot);
            await RefreshAfterRemoteOperationAsync();
        }
        catch (ForcePushWithLeaseCancelledException exception)
        {
            await RefreshAfterRemoteOperationAsync();
            await ShowErrorAsync("Force push cancelled", exception.Message);
        }
        catch (PushRejectedException exception) when (exception.ResultKind == PushResultKind.LeaseRejected)
        {
            await RefreshAfterRemoteOperationAsync();
            var rejection = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Force push rejected",
                Content = $"{snapshot.Remote}/{snapshot.RemoteBranch} changed after it was checked.\n\nExpected: {ShortOid(snapshot.ExpectedRemoteCommit)}\n\nThe remote branch contains a different state. Your force push was not performed.",
                PrimaryButtonText = "Fetch",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close
            };
            if (await rejection.ShowAsync() == ContentDialogResult.Primary)
            {
                await _referenceService.FetchAsync(repository, snapshot.Remote);
                await RefreshAfterRemoteOperationAsync();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RefreshAfterRemoteOperationAsync();
            await ShowErrorAsync("Force push failed", exception.Message);
        }
    }

    private async Task ShowForcePreparationFailureAsync(Exception exception)
    {
        if (exception is ForcePushWithLeasePreparationException preparation)
        {
            var title = preparation.Failure switch
            {
                ForcePushPreparationFailure.RemoteBranchDoesNotExist => "Remote branch does not exist",
                ForcePushPreparationFailure.MultiplePushDestinations => "Force push with lease unavailable",
                _ => "Force push with lease unavailable"
            };
            await ShowErrorAsync(title, preparation.Message);
            return;
        }
        await ShowErrorAsync("Force push with lease unavailable", exception.Message);
    }

    private async Task RefreshAfterRemoteOperationAsync()
    {
        await _viewModel.RefreshAsyncForDesktopCheck();
        RefreshPresentationCollections();
    }

    private static string ShortOid(string oid) => oid[..Math.Min(10, oid.Length)];
    private static string FormatSubject(string? subject) => string.IsNullOrWhiteSpace(subject) ? string.Empty : $"  {subject}";

    private async void ForcePushWithLease_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button)
        {
            GitOperationsDialog.Hide();
            await Task.Delay(20);
        }
        await RunForcePushWithLeaseAsync();
    }
}
''')

# Ordinary Push from the toolbar/dialog must surface typed non-fast-forward rejection.
replace_once(
    "src/CSharpGit.Presentation/MainPage.xaml.cs",
    "    private async void Push_Click(object sender, RoutedEventArgs e) => await ExecuteCommandAsync(_viewModel.PushCommand);\n",
    "    private async void Push_Click(object sender, RoutedEventArgs e)\n"
    "    {\n"
    "        if (sender is Button)\n"
    "        {\n"
    "            GitOperationsDialog.Hide();\n"
    "            await Task.Delay(20);\n"
    "        }\n"
    "        await PushFromUiAsync();\n"
    "    }\n")

# Explicit target area and toolbar expose a separate force-with-lease action only.
xaml_path = "src/CSharpGit.Presentation/MainPage.xaml"
replace_once(
    xaml_path,
    '''          <StackPanel Orientation="Horizontal" Spacing="8">\n            <Button Content="Push" Command="{Binding PushCommand}" />\n            <Button Content="Fetch all" Command="{Binding FetchAllCommand}" />\n          </StackPanel>'''.replace("\\n", "\n"),
    '''          <StackPanel Orientation="Horizontal" Spacing="8">\n            <Button Content="Push" Click="Push_Click" />\n            <Button Content="Force push with lease…" Click="ForcePushWithLease_Click" />\n            <Button Content="Fetch all" Command="{Binding FetchAllCommand}" />\n          </StackPanel>'''.replace("\\n", "\n"))
replace_once(
    xaml_path,
    '''            <Button Click="Push_Click" Padding="8,5">\n              <StackPanel Orientation="Horizontal" Spacing="5"><FontIcon Glyph="&#xE898;" FontSize="13" /><TextBlock Text="Push" /></StackPanel>\n            </Button>'''.replace("\\n", "\n"),
    '''            <Button Padding="8,5">\n              <StackPanel Orientation="Horizontal" Spacing="5"><FontIcon Glyph="&#xE898;" FontSize="13" /><TextBlock Text="Push ▼" /></StackPanel>\n              <Button.Flyout>\n                <MenuFlyout>\n                  <MenuFlyoutItem Text="Push" Click="Push_Click" />\n                  <MenuFlyoutSeparator />\n                  <MenuFlyoutItem Text="Force push with lease…" Click="ForcePushWithLease_Click" />\n                </MenuFlyout>\n              </Button.Flyout>\n            </Button>'''.replace("\\n", "\n"))

# Existing push failure test now verifies the classified failure type.
tests_path = "tests/CSharpGit.Git.Tests/GitCliRepositoryServiceTests.cs"
replace_once(
    tests_path,
    "        var authenticationFailure = await Assert.ThrowsAsync<CSharpGit.Application.Exceptions.RepositoryOpenException>(\n            () => service.PushAsync(repository));\n        Assert.Contains(\"Authentication failed\", authenticationFailure.Message, StringComparison.OrdinalIgnoreCase);\n",
    "        var authenticationFailure = await Assert.ThrowsAsync<CSharpGit.Application.Exceptions.PushRejectedException>(\n            () => service.PushAsync(repository));\n        Assert.Contains(\"Authentication failed\", authenticationFailure.Message, StringComparison.OrdinalIgnoreCase);\n        Assert.Equal(CSharpGit.Application.Exceptions.PushResultKind.AuthenticationOrTransportFailure, authenticationFailure.ResultKind);\n")

write("tests/CSharpGit.Git.Tests/ForcePushWithLeaseTests.cs", r'''using System.Diagnostics;
using CSharpGit.Application.Exceptions;
using CSharpGit.Domain;

namespace CSharpGit.Git.Tests;

public sealed class ForcePushWithLeaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-force-{Guid.NewGuid():N}");
    private readonly string _remote;

    public ForcePushWithLeaseTests()
    {
        Directory.CreateDirectory(_root);
        Git(_root, "init", "-b", "main");
        ConfigureIdentity(_root);
        Commit(_root, "history.txt", "A\n", "A");
        _remote = Path.Combine(_root, ".remote.git");
        Git(_root, "init", "--bare", _remote);
        Git(_root, "remote", "add", "origin", _remote);
    }

    [Fact]
    public void BuildsOnlyExplicitLeaseAndFullRefspec()
    {
        const string expected = "0123456789abcdef0123456789abcdef01234567";
        var snapshot = new ForcePushWithLeaseSnapshot(
            "feature/local-name", expected, "origin", "/tmp/remote.git",
            "feature/server-name", expected);

        var arguments = GitCliRepositoryService.BuildForcePushArguments(snapshot);

        Assert.Equal("push", arguments[0]);
        Assert.Contains("--porcelain", arguments);
        Assert.Contains($"--force-with-lease=refs/heads/feature/server-name:{expected}", arguments);
        Assert.Contains("refs/heads/feature/local-name:refs/heads/feature/server-name", arguments);
        Assert.DoesNotContain("--force", arguments);
        Assert.DoesNotContain("-f", arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith('+'));
        Assert.DoesNotContain("--force-with-lease", arguments);
        Assert.DoesNotContain("--force-with-lease=refs/heads/feature/server-name", arguments);
    }

    [Fact]
    public async Task SucceedsWithConfiguredUpstreamAndDifferentBranchNames()
    {
        Commit(_root, "history.txt", "A\nB\n", "B");
        Commit(_root, "history.txt", "A\nB\nC\n", "C");
        Git(_root, "push", "origin", "refs/heads/main:refs/heads/server-main");
        Git(_root, "config", "branch.main.remote", "origin");
        Git(_root, "config", "branch.main.merge", "refs/heads/server-main");
        var oldRemote = RemoteTip("server-main");
        Git(_root, "reset", "--hard", "HEAD~2");
        Commit(_root, "history.txt", "A\nB2\n", "B2");
        Commit(_root, "history.txt", "A\nB2\nC2\n", "C2");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var snapshot = await service.PrepareForcePushWithLeaseAsync(repository);

        Assert.Equal("main", snapshot.LocalBranch);
        Assert.Equal("origin", snapshot.Remote);
        Assert.Equal("server-main", snapshot.RemoteBranch);
        Assert.Equal(oldRemote, snapshot.ExpectedRemoteCommit);
        Assert.Equal(Path.GetFullPath(_remote), Path.GetFullPath(snapshot.RemotePushDestination));

        await service.ForcePushWithLeaseAsync(repository, snapshot);

        Assert.Equal(GitOut(_root, "rev-parse", "refs/heads/main"), RemoteTip("server-main"));
    }

    [Fact]
    public async Task RejectsOldSnapshotWhenAnotherActorAdvancesRemote()
    {
        Commit(_root, "history.txt", "A\nB\n", "B");
        Commit(_root, "history.txt", "A\nB\nC\n", "C");
        Git(_root, "push", "--set-upstream", "origin", "main");
        Git(_root, "reset", "--hard", "HEAD~2");
        Commit(_root, "history.txt", "A\nB2\n", "B2");
        Commit(_root, "history.txt", "A\nB2\nC2\n", "C2");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var snapshot = await service.PrepareForcePushWithLeaseAsync(repository);
        var actor = Path.Combine(_root, "actor");
        Git(_root, "clone", "--branch", "main", _remote, actor);
        ConfigureIdentity(actor);
        File.AppendAllText(Path.Combine(actor, "history.txt"), "D\n");
        Git(actor, "add", "history.txt");
        Git(actor, "commit", "-m", "D");
        Git(actor, "push", "origin", "main");
        var advancedRemote = RemoteTip("main");

        var failure = await Assert.ThrowsAsync<PushRejectedException>(
            () => service.ForcePushWithLeaseAsync(repository, snapshot));

        Assert.Equal(PushResultKind.LeaseRejected, failure.ResultKind);
        Assert.Equal(advancedRemote, RemoteTip("main"));
        Assert.NotEqual(snapshot.ExpectedRemoteCommit, advancedRemote);
    }

    [Fact]
    public async Task MissingUpstreamRequiresExplicitTargetAndDoesNotGuess()
    {
        Git(_root, "push", "origin", "refs/heads/main:refs/heads/server-main");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);

        var missing = await Assert.ThrowsAsync<ForcePushWithLeasePreparationException>(
            () => service.PrepareForcePushWithLeaseAsync(repository));
        Assert.Equal(ForcePushPreparationFailure.MissingUpstream, missing.Failure);

        var explicitSnapshot = await service.PrepareForcePushWithLeaseAsync(repository, "origin", "server-main");
        Assert.Equal("server-main", explicitSnapshot.RemoteBranch);
    }

    [Fact]
    public async Task RefusesDetachedHeadMissingRemoteBranchAndMultiplePushDestinations()
    {
        Git(_root, "push", "origin", "main");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);

        var missing = await Assert.ThrowsAsync<ForcePushWithLeasePreparationException>(
            () => service.PrepareForcePushWithLeaseAsync(repository, "origin", "missing"));
        Assert.Equal(ForcePushPreparationFailure.RemoteBranchDoesNotExist, missing.Failure);

        var secondRemote = Path.Combine(_root, ".second.git");
        Git(_root, "init", "--bare", secondRemote);
        Git(_root, "remote", "set-url", "--add", "--push", "origin", _remote);
        Git(_root, "remote", "set-url", "--add", "--push", "origin", secondRemote);
        var multiple = await Assert.ThrowsAsync<ForcePushWithLeasePreparationException>(
            () => service.PrepareForcePushWithLeaseAsync(repository, "origin", "main"));
        Assert.Equal(ForcePushPreparationFailure.MultiplePushDestinations, multiple.Failure);

        Git(_root, "remote", "set-url", "--delete", "--push", "origin", secondRemote);
        Git(_root, "checkout", "--detach", "HEAD");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PrepareForcePushWithLeaseAsync(repository, "origin", "main"));
    }

    [Fact]
    public async Task CancelsBeforePushWhenLocalBranchOrTipChanges()
    {
        Git(_root, "push", "--set-upstream", "origin", "main");
        var originalRemote = RemoteTip("main");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var branchSnapshot = await service.PrepareForcePushWithLeaseAsync(repository);
        Git(_root, "switch", "-c", "other");

        await Assert.ThrowsAsync<ForcePushWithLeaseCancelledException>(
            () => service.ForcePushWithLeaseAsync(repository, branchSnapshot));
        Assert.Equal(originalRemote, RemoteTip("main"));

        Git(_root, "switch", "main");
        var tipSnapshot = await service.PrepareForcePushWithLeaseAsync(repository);
        Commit(_root, "tip.txt", "changed\n", "local tip changed");
        await Assert.ThrowsAsync<ForcePushWithLeaseCancelledException>(
            () => service.ForcePushWithLeaseAsync(repository, tipSnapshot));
        Assert.Equal(originalRemote, RemoteTip("main"));
    }

    [Fact]
    public async Task CancelsBeforePushWhenPushDestinationChanges()
    {
        Git(_root, "push", "--set-upstream", "origin", "main");
        var originalRemote = RemoteTip("main");
        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var snapshot = await service.PrepareForcePushWithLeaseAsync(repository);
        var replacement = Path.Combine(_root, ".replacement.git");
        Git(_root, "init", "--bare", replacement);
        Git(_root, "remote", "set-url", "--push", "origin", replacement);

        await Assert.ThrowsAsync<ForcePushWithLeaseCancelledException>(
            () => service.ForcePushWithLeaseAsync(repository, snapshot));
        Assert.Equal(originalRemote, RemoteTip("main"));
    }

    [Fact]
    public async Task OrdinaryPushRemainsOrdinaryAndClassifiesNonFastForward()
    {
        Git(_root, "push", "--set-upstream", "origin", "main");
        var actor = Path.Combine(_root, "ordinary-actor");
        Git(_root, "clone", "--branch", "main", _remote, actor);
        ConfigureIdentity(actor);
        Commit(actor, "actor.txt", "remote\n", "remote advance");
        Git(actor, "push", "origin", "main");
        Commit(_root, "local.txt", "local\n", "local divergence");

        var service = new GitCliRepositoryService();
        var repository = await service.OpenAsync(_root);
        var failure = await Assert.ThrowsAsync<PushRejectedException>(() => service.PushAsync(repository));

        Assert.Equal(PushResultKind.NonFastForwardRejected, failure.ResultKind);
        Assert.Equal(GitOut(actor, "rev-parse", "HEAD"), RemoteTip("main"));
    }

    [Theory]
    [InlineData("remote: Authentication failed", PushResultKind.AuthenticationOrTransportFailure)]
    [InlineData("! [rejected] main -> main (stale info)", PushResultKind.LeaseRejected)]
    [InlineData("! [rejected] main -> main (non-fast-forward)", PushResultKind.NonFastForwardRejected)]
    [InlineData("! [remote rejected] main -> main (pre-receive hook declined)", PushResultKind.RemoteRejected)]
    public void ClassifiesPushFailuresWithoutConfusingRemotePrefix(string message, PushResultKind expected)
    {
        Assert.Equal(expected, GitCliRepositoryService.ClassifyPushFailure(message));
    }

    private string RemoteTip(string branch) => GitOut(_root, "--git-dir", _remote, "rev-parse", $"refs/heads/{branch}");

    private static void Commit(string directory, string name, string content, string message)
    {
        File.WriteAllText(Path.Combine(directory, name), content);
        Git(directory, "add", name);
        Git(directory, "commit", "-m", message);
    }

    private static void ConfigureIdentity(string directory)
    {
        Git(directory, "config", "user.email", "tests@example.invalid");
        Git(directory, "config", "user.name", "CSharpGit Tests");
    }

    private static void Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
    }

    private static string GitOut(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
        return output.Trim();
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
''')

write("tests/CSharpGit.Desktop.Tests/ForcePushUiContractTests.cs", r'''namespace CSharpGit.Desktop.Tests;

public sealed class ForcePushUiContractTests
{
    [Fact]
    public void MainPageExposesSeparateLeaseOnlyForceWorkflow()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var workflow = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.ForcePush.cs"));

        Assert.Contains("Push ▼", xaml, StringComparison.Ordinal);
        Assert.Contains("Force push with lease…", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ForcePushWithLease_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PrepareForcePushWithLeaseAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("ForcePushWithLeaseAsync(repository, snapshot)", workflow, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Force push with lease\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Force anyway", xaml + workflow, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
''')

# Durable IDD intent.
write(".idd/intent/IDD-0013.spec-force-push-with-lease.md", r'''# IDD-0013.spec-force-push-with-lease

## Intent

CSharpGit поддерживает безопасное переписывание опубликованной branch после rebase,
amend и других изменений истории только через Git force-with-lease с exact expected
remote commit. Unsafe force push не является функцией продукта.

## Behavior

- Toolbar предоставляет обычный Push и отдельный `Force push with lease…`.
- Ordinary Push никогда автоматически не превращается в force operation.
- Force workflow состоит из Prepare → Confirmation → Execute.
- Prepare определяет current local branch и exact local OID; configured upstream
  читается из `branch.<name>.remote` и `branch.<name>.merge`. При отсутствии upstream
  пользователь обязан явно задать remote и remote branch; приложение их не угадывает.
- Actual expected remote OID получается сетевым `ls-remote` именно с единственной
  configured push destination. Remote-tracking refs не используются как fallback.
- Prepare создаёт immutable snapshot с local branch/OID, remote, push destination,
  remote branch и full expected remote OID. Confirmation показывает этот snapshot.
- Execute использует тот же snapshot, повторно проверяет local branch, local tip и
  push destination, но не запрашивает новый remote OID.
- Git invocation содержит `--porcelain`, exact
  `--force-with-lease=refs/heads/<remote>:<expected>` и full source/destination refs.
- Если remote изменился после Prepare, Git lease отклоняет Push; CSharpGit не retry,
  не обновляет expected OID автоматически и не предлагает unsafe fallback.
- Missing remote branch не создаётся force workflow: для создания используется
  ordinary Push. Несколько push destinations не поддерживаются force workflow.
- Push failures классифицируются минимум как non-fast-forward, lease, remote-policy,
  authentication/transport и other; классификация не влияет на safety invariant.
- После success/failure обновляется локальное repository state. Fetch после rejection
  выполняется только по явному действию пользователя.

## Safety Invariants

- В application API и Git service отсутствует `force` boolean/mode, допускающий
  unsafe force.
- Force workflow никогда не формирует `--force`, `-f`, `+refspec`, implicit
  expected-less `--force-with-lease` или fallback на них.
- `confirmed snapshot == executed snapshot`.
- Если actual remote OID отличается от `snapshot.ExpectedRemoteCommit`, remote branch
  не должна быть перезаписана.

## Verification

- Реальные bare-repository tests покрывают successful rewrite и critical race C→D.
- Проверяются exact lease argument, full refspec и отсутствие unsafe force forms.
- Проверяются detached HEAD, missing upstream/explicit target, different local/remote
  names, missing remote branch, multiple push URLs, изменения local branch/tip и push
  destination после Prepare.
- Ordinary non-fast-forward Push остаётся ordinary и классифицируется для contextual
  перехода в новый force-with-lease workflow; автоматического force execution нет.
- UI contract проверяет отдельное название/action и передачу exact snapshot из
  confirmation в Execute.

## Non-Goals

Unsafe force push, automatic force/retry, `Force anyway`, force tags, arbitrary
refspec editor, multi-branch/multi-remote force push, multiple push destinations,
`--force-if-includes` и создание remote branch через force workflow.
''')

index_path = ".idd/intent/INDEX.md"
replace_once(
    index_path,
    "| IDD-0012 | Spec | Repository tree navigation | Branch ordering, compact initial expansion and session expansion state | — |",
    "| IDD-0012 | Spec | Repository tree navigation | Branch ordering, compact initial expansion and session expansion state | — |\n"
    "| IDD-0013 | Spec | Safe force push | Explicit force-with-lease snapshot, confirmation and CAS safety | — |")
