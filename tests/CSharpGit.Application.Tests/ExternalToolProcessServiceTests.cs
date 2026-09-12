using System.Diagnostics;
using System.Reflection;
using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class ExternalToolProcessEnvironmentCollection
{
    public const string CollectionName = "External tool process environment";
}

[Collection(ExternalToolProcessEnvironmentCollection.CollectionName)]
public sealed class ExternalToolProcessServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csharpgit-external-tool-{Guid.NewGuid():N}");
    private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");
    private readonly string? _originalPathExt = Environment.GetEnvironmentVariable("PATHEXT");

    [Fact]
    public void ResolvesQuotedExecutableWithSpacesAndUnicodePath()
    {
        Directory.CreateDirectory(_root);
        var executable = Path.Combine(_root, OperatingSystem.IsWindows() ? "тест tool.exe" : "тест tool");
        File.WriteAllText(executable, string.Empty);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var service = new ExternalToolProcessService();
        var resolved = service.ResolveExecutable($"\"{executable}\" --wait");

        Assert.Equal(Path.GetFullPath(executable), resolved);
    }

    [Fact]
    public void ResolvesBareExecutableFromCurrentPathRules()
    {
        Directory.CreateDirectory(_root);
        const string command = "csharpgit-tool-test";
        var fileName = OperatingSystem.IsWindows() ? $"{command}.cmd" : command;
        var executable = Path.Combine(_root, fileName);
        File.WriteAllText(executable, OperatingSystem.IsWindows() ? "@echo off\r\n" : "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Environment.SetEnvironmentVariable("PATH", _root);
        if (OperatingSystem.IsWindows()) Environment.SetEnvironmentVariable("PATHEXT", ".CMD;.EXE");

        var service = new ExternalToolProcessService();
        var resolved = service.ResolveExecutable(command);

        Assert.Equal(Path.GetFullPath(executable), resolved);
    }

    [Fact]
    public void ShellConstructionKeepsUnicodeAndSpaceFilePathAsOneArgument()
    {
        Directory.CreateDirectory(_root);
        var filePath = Path.Combine(_root, "folder with spaces", "данные 日本語.txt");
        var workingDirectory = _root;
        var method = typeof(ExternalToolProcessService).GetMethod(
            "CreateShellStartInfo",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CreateShellStartInfo was not found.");

        var startInfo = Assert.IsType<ProcessStartInfo>(method.Invoke(null, ["editor --wait", filePath, workingDirectory]));

        Assert.Equal(workingDirectory, startInfo.WorkingDirectory);
        if (OperatingSystem.IsWindows())
        {
            Assert.EndsWith("cmd.exe", startInfo.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new[] { "/d", "/s", "/c", $"editor --wait \"{filePath}\"" }, startInfo.ArgumentList.ToArray());
        }
        else
        {
            Assert.Equal("/bin/sh", startInfo.FileName);
            Assert.Equal(new[] { "-c", $"editor --wait '{filePath}'" }, startInfo.ArgumentList.ToArray());
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        Environment.SetEnvironmentVariable("PATHEXT", _originalPathExt);
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
