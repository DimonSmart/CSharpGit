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
        raise RuntimeError(f"{path}: expected one occurrence, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


vm = "src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.cs"
replace_once(
    vm,
    "    public Repository? Repository { get => _repository; private set { _repository = value; Notify(); Notify(nameof(RepositoryVisibility)); Notify(nameof(PickerVisibility)); Notify(nameof(RepositoryKind)); ((AsyncCommand)RefreshHistoryCommand).RaiseCanExecuteChanged(); } }",
    "    public Repository? Repository { get => _repository; private set { _repository = value; Notify(); Notify(nameof(RepositoryVisibility)); Notify(nameof(PickerVisibility)); Notify(nameof(RepositoryKind)); Notify(nameof(CanForcePushWithLease)); ((AsyncCommand)RefreshHistoryCommand).RaiseCanExecuteChanged(); } }")
replace_once(
    vm,
    "    public bool IsBusy { get => _isBusy; private set { _isBusy = value; Notify(); Notify(nameof(BusyVisibility)); _openRepositoryCommand.RaiseCanExecuteChanged(); ((AsyncCommand)RefreshHistoryCommand).RaiseCanExecuteChanged(); ((AsyncCommand)LoadMoreCommand).RaiseCanExecuteChanged(); } }",
    "    public bool IsBusy { get => _isBusy; private set { _isBusy = value; Notify(); Notify(nameof(BusyVisibility)); Notify(nameof(CanForcePushWithLease)); _openRepositoryCommand.RaiseCanExecuteChanged(); ((AsyncCommand)RefreshHistoryCommand).RaiseCanExecuteChanged(); ((AsyncCommand)LoadMoreCommand).RaiseCanExecuteChanged(); } }")
replace_once(
    vm,
    "    public RepositoryOperation CurrentOperation { get => _currentOperation; private set { _currentOperation = value; Notify(); RaiseCommands(); } }",
    "    public RepositoryOperation CurrentOperation { get => _currentOperation; private set { _currentOperation = value; Notify(); Notify(nameof(CanForcePushWithLease)); RaiseCommands(); } }")
replace_once(
    vm,
    "    public Visibility OperationVisibility => OperationState.Kind == RepositoryOperation.None ? Visibility.Collapsed : Visibility.Visible;",
    "    public Visibility OperationVisibility => OperationState.Kind == RepositoryOperation.None ? Visibility.Collapsed : Visibility.Visible;\n    public bool CanForcePushWithLease => Repository is not null && !IsBusy && CurrentOperation == RepositoryOperation.None && LocalBranches.Any(branch => branch.IsCurrent);")
replace_once(
    vm,
    "        Replace(LocalBranches, state.Refs.LocalBranches);",
    "        Replace(LocalBranches, state.Refs.LocalBranches);\n        Notify(nameof(CanForcePushWithLease));")

xaml = "src/CSharpGit.Presentation/MainPage.xaml"
replace_once(
    xaml,
    "            <Button Content=\"Force push with lease…\" Click=\"ForcePushWithLease_Click\" />",
    "            <Button Content=\"Force push with lease…\" Click=\"ForcePushWithLease_Click\" IsEnabled=\"{Binding CanForcePushWithLease}\" />")
replace_once(
    xaml,
    "                  <MenuFlyoutItem Text=\"Force push with lease…\" Click=\"ForcePushWithLease_Click\" />",
    "                  <MenuFlyoutItem Text=\"Force push with lease…\" Click=\"ForcePushWithLease_Click\" IsEnabled=\"{Binding CanForcePushWithLease}\" />")

ui_test = "tests/CSharpGit.Application.Tests/DesktopUiContractTests.cs"
replace_once(
    ui_test,
    "        Assert.Contains(\"ForcePushWithLeaseAsync(repository, snapshot)\", forcePushPage);\n",
    "        Assert.Contains(\"ForcePushWithLeaseAsync(repository, snapshot)\", forcePushPage);\n"
    "        Assert.Equal(2, Count(xaml, \"IsEnabled=\\\"{Binding CanForcePushWithLease}\\\"\"));\n"
    "        Assert.Contains(\"CanForcePushWithLease => Repository is not null && !IsBusy\", viewModel);\n"
    "        Assert.Contains(\"CurrentOperation == RepositoryOperation.None\", viewModel);\n"
    "        Assert.Contains(\"LocalBranches.Any(branch => branch.IsCurrent)\", viewModel);\n")

write("tests/CSharpGit.Git.Tests/ForcePushWithLeaseRetryTests.cs", r'''using System.Diagnostics;
using CSharpGit.Application.Exceptions;

namespace CSharpGit.Git.Tests;

public sealed class ForcePushWithLeaseRetryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-force-count-{Guid.NewGuid():N}");
    private readonly string _remote;
    private readonly string _pushLog;

    public ForcePushWithLeaseRetryTests()
    {
        Directory.CreateDirectory(_root);
        Git(_root, "init", "-b", "main");
        ConfigureIdentity(_root);
        Commit(_root, "history.txt", "A\n", "A");
        _remote = Path.Combine(_root, ".remote.git");
        _pushLog = Path.Combine(_root, "push-invocations.log");
        Git(_root, "init", "--bare", _remote);
        Git(_root, "remote", "add", "origin", _remote);
    }

    [Fact]
    public async Task LeaseRejectionExecutesExactlyOneGitPush()
    {
        if (OperatingSystem.IsWindows()) return;

        Commit(_root, "history.txt", "A\nB\n", "B");
        Commit(_root, "history.txt", "A\nB\nC\n", "C");
        Git(_root, "push", "--set-upstream", "origin", "main");
        Git(_root, "reset", "--hard", "HEAD~2");
        Commit(_root, "history.txt", "A\nB2\n", "B2");
        Commit(_root, "history.txt", "A\nB2\nC2\n", "C2");

        var normalService = new GitCliRepositoryService();
        var repository = await normalService.OpenAsync(_root);
        var snapshot = await normalService.PrepareForcePushWithLeaseAsync(repository);

        var actor = Path.Combine(_root, "actor");
        Git(_root, "clone", "--branch", "main", _remote, actor);
        ConfigureIdentity(actor);
        File.AppendAllText(Path.Combine(actor, "history.txt"), "D\n");
        Git(actor, "add", "history.txt");
        Git(actor, "commit", "-m", "D");
        Git(actor, "push", "origin", "main");
        var advancedRemote = RemoteTip("main");

        var wrapper = Path.Combine(_root, "counting-git.sh");
        File.WriteAllText(wrapper,
            $"#!/bin/sh\nif [ \"$1\" = \"push\" ]; then printf 'push\\n' >> '{_pushLog}'; fi\nexec git \"$@\"\n");
        File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var instrumentedService = new GitCliRepositoryService(new GitCliOptions { ExecutablePath = wrapper });
        var instrumentedRepository = await instrumentedService.OpenAsync(_root);
        var failure = await Assert.ThrowsAsync<PushRejectedException>(
            () => instrumentedService.ForcePushWithLeaseAsync(instrumentedRepository, snapshot));

        Assert.Equal(PushResultKind.LeaseRejected, failure.ResultKind);
        Assert.True(File.Exists(_pushLog));
        Assert.Equal(1, File.ReadAllLines(_pushLog).Length);
        Assert.Equal(advancedRemote, RemoteTip("main"));
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
