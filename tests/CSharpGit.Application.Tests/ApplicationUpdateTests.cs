using System.Net;
using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;

namespace CSharpGit.Application.Tests;

public sealed class ApplicationUpdateTests
{
    [Theory]
    [InlineData("0.1.10", 0, 1, 10)]
    [InlineData("v0.1.10", 0, 1, 10)]
    [InlineData("0.1.10+metadata", 0, 1, 10)]
    public void ReleaseVersionParserAcceptsSupportedVersions(
        string value,
        int major,
        int minor,
        int patch)
    {
        Assert.True(ReleaseVersionParser.TryParse(value, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-beta")]
    public void ReleaseVersionParserRejectsMalformedVersions(string value) =>
        Assert.False(ReleaseVersionParser.TryParse(value, out _));

    [Fact]
    public void ReleaseVersionComparisonIsNumeric() =>
        Assert.True(new ReleaseVersion(0, 1, 9) < new ReleaseVersion(0, 1, 10));

    [Fact]
    public async Task NewerGitHubReleaseIsReportedAsAvailable()
    {
        string? userAgent = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(JsonResponse(
                """{"tag_name":"v0.1.11","html_url":"https://github.com/DimonSmart/CSharpGit/releases/tag/v0.1.11"}"""));
        }));
        using var service = new GitHubUpdateCheckService(
            new StubVersionProvider(new ReleaseVersion(0, 1, 10)),
            httpClient,
            TimeSpan.FromSeconds(1));

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new ReleaseVersion(0, 1, 11), result.LatestVersion);
        Assert.Equal("CSharpGit/0.1.10", userAgent);
        Assert.Equal(
            "https://github.com/DimonSmart/CSharpGit/releases/tag/v0.1.11",
            result.ReleasePageUri?.AbsoluteUri);
    }

    [Fact]
    public async Task EqualOrOlderGitHubReleaseIsReportedAsUpToDate()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """{"tag_name":"v0.1.10","html_url":"https://github.com/DimonSmart/CSharpGit/releases/tag/v0.1.10"}"""))));
        using var service = new GitHubUpdateCheckService(
            new StubVersionProvider(new ReleaseVersion(0, 1, 10)),
            httpClient,
            TimeSpan.FromSeconds(1));

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task HttpAndMalformedResponsesAreUnavailable()
    {
        using var errorClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        using var errorService = new GitHubUpdateCheckService(
            new StubVersionProvider(new ReleaseVersion(0, 1, 10)),
            errorClient,
            TimeSpan.FromSeconds(1));

        Assert.Equal(UpdateCheckStatus.Unavailable, (await errorService.CheckAsync()).Status);

        using var invalidClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("""{"tag_name":42}"""))));
        using var invalidService = new GitHubUpdateCheckService(
            new StubVersionProvider(new ReleaseVersion(0, 1, 10)),
            invalidClient,
            TimeSpan.FromSeconds(1));

        Assert.Equal(UpdateCheckStatus.Unavailable, (await invalidService.CheckAsync()).Status);
    }

    [Fact]
    public async Task UpdateCheckTimeoutIsUnavailable()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return JsonResponse("{}");
        }));
        using var service = new GitHubUpdateCheckService(
            new StubVersionProvider(new ReleaseVersion(0, 1, 10)),
            httpClient,
            TimeSpan.FromMilliseconds(20));

        var result = await service.CheckAsync();

        Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return JsonResponse("{}");
        }));
        using var service = new GitHubUpdateCheckService(
            new StubVersionProvider(new ReleaseVersion(0, 1, 10)),
            httpClient,
            TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckAsync(cancellation.Token));
    }

    [Fact]
    public async Task MacUpdaterIsAvailableOnlyForStandardHomebrewInstallation()
    {
        var environment = CreateMacEnvironment();
        var executor = new QueueProcessExecutor(Success());
        var installer = new MacOsHomebrewUpdateInstaller(executor, environment);

        var available = await installer.GetAvailabilityAsync();

        Assert.True(available.IsAvailable);
        Assert.Equal("/opt/homebrew/bin/brew", executor.Calls.Single().Executable);
        Assert.Equal(new[] { "list", "--cask", MacOsHomebrewUpdateInstaller.CaskName }, executor.Calls.Single().Arguments);

        environment.IsMacOS = false;
        Assert.False((await new MacOsHomebrewUpdateInstaller(
            new QueueProcessExecutor(),
            environment).GetAvailabilityAsync()).IsAvailable);

        environment.IsMacOS = true;
        environment.ProcessPath = "/tmp/CSharpGit/bin/Debug/CSharpGit";
        Assert.False((await new MacOsHomebrewUpdateInstaller(
            new QueueProcessExecutor(),
            environment).GetAvailabilityAsync()).IsAvailable);
    }

    [Fact]
    public async Task MissingHomebrewOrCaskMakesUpdaterUnavailable()
    {
        var missingBrew = CreateMacEnvironment();
        missingBrew.ExecutableFiles.Clear();
        Assert.False((await new MacOsHomebrewUpdateInstaller(
            new QueueProcessExecutor(),
            missingBrew).GetAvailabilityAsync()).IsAvailable);

        var missingCask = CreateMacEnvironment();
        Assert.False((await new MacOsHomebrewUpdateInstaller(
            new QueueProcessExecutor(Failure()),
            missingCask).GetAvailabilityAsync()).IsAvailable);
    }

    [Fact]
    public async Task MacUpdaterRunsTargetedUpgradeVerificationQuarantineAndRelaunch()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new QueueProcessExecutor(
            Success(),
            Success(),
            Success("""{"casks":[{"version":"0.1.11"}]}"""),
            Success(),
            Success("0.1.11\n"),
            Success(),
            Failure(),
            Success());
        var installer = new MacOsHomebrewUpdateInstaller(executor, CreateMacEnvironment());

        var result = await installer.InstallAsync(
            new ReleaseVersion(0, 1, 11),
            cancellationToken: cancellation.Token);

        Assert.Equal(ApplicationUpdateResultStatus.Succeeded, result.Status);
        Assert.Equal(new[] { "update" }, executor.Calls[1].Arguments);
        Assert.Equal(
            new[] { "info", "--cask", "--json=v2", MacOsHomebrewUpdateInstaller.CaskName },
            executor.Calls[2].Arguments);
        Assert.Equal(
            new[]
            {
                "upgrade",
                "--cask",
                "--no-quit",
                "--appdir=/Applications",
                MacOsHomebrewUpdateInstaller.CaskName
            },
            executor.Calls[3].Arguments);
        Assert.False(executor.Calls[3].CanBeCanceled);
        Assert.Equal("/usr/bin/plutil", executor.Calls[4].Executable);
        Assert.Contains("CFBundleShortVersionString", executor.Calls[4].Arguments);
        Assert.Equal("/usr/bin/xattr", executor.Calls[5].Executable);
        Assert.Equal("/usr/bin/xattr", executor.Calls[6].Executable);
        Assert.Equal("/usr/bin/open", executor.Calls[7].Executable);
    }

    [Fact]
    public async Task PublicationLagDoesNotInstallOlderCask()
    {
        var executor = new QueueProcessExecutor(
            Success(),
            Success(),
            Success("""{"casks":[{"version":"0.1.10"}]}"""));
        var installer = new MacOsHomebrewUpdateInstaller(executor, CreateMacEnvironment());

        var result = await installer.InstallAsync(new ReleaseVersion(0, 1, 11));

        Assert.Equal(ApplicationUpdateResultStatus.PackageNotPublished, result.Status);
        Assert.DoesNotContain(executor.Calls, call => call.Arguments.Contains("upgrade"));
    }

    [Fact]
    public async Task MalformedHomebrewInfoDoesNotStartUpgrade()
    {
        var executor = new QueueProcessExecutor(
            Success(),
            Success(),
            Success("""{"casks":[]}"""));
        var installer = new MacOsHomebrewUpdateInstaller(executor, CreateMacEnvironment());

        var result = await installer.InstallAsync(new ReleaseVersion(0, 1, 11));

        Assert.Equal(ApplicationUpdateResultStatus.Failed, result.Status);
        Assert.DoesNotContain(executor.Calls, call => call.Arguments.Contains("upgrade"));
    }

    [Fact]
    public async Task AlreadyUpToDateOutputStillUsesInstalledVersionVerification()
    {
        var executor = new QueueProcessExecutor(
            Success(),
            Success(),
            Success("""{"casks":[{"version":"0.1.11"}]}"""),
            Failure("already up-to-date"),
            Success("0.1.11"),
            Success(),
            Failure(),
            Success());
        var installer = new MacOsHomebrewUpdateInstaller(executor, CreateMacEnvironment());

        var result = await installer.InstallAsync(new ReleaseVersion(0, 1, 11));

        Assert.Equal(ApplicationUpdateResultStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task QuarantineFailureLeavesCurrentInstanceResponsibleForRecovery()
    {
        var executor = new QueueProcessExecutor(
            Success(),
            Success(),
            Success("""{"casks":[{"version":"0.1.11"}]}"""),
            Success(),
            Success("0.1.11"),
            Failure());
        var installer = new MacOsHomebrewUpdateInstaller(executor, CreateMacEnvironment());

        var result = await installer.InstallAsync(new ReleaseVersion(0, 1, 11));

        Assert.Equal(ApplicationUpdateResultStatus.QuarantineFailed, result.Status);
        Assert.Contains("/usr/bin/xattr -dr com.apple.quarantine", result.Message);
        Assert.DoesNotContain(executor.Calls, call => call.Executable == "/usr/bin/open");
    }

    [Fact]
    public async Task RelaunchFailureIsReportedWithoutPretendingSuccess()
    {
        var executor = new QueueProcessExecutor(
            Success(),
            Success(),
            Success("""{"casks":[{"version":"0.1.11"}]}"""),
            Success(),
            Success("0.1.11"),
            Success(),
            Failure(),
            Failure());
        var installer = new MacOsHomebrewUpdateInstaller(executor, CreateMacEnvironment());

        var result = await installer.InstallAsync(new ReleaseVersion(0, 1, 11));

        Assert.Equal(ApplicationUpdateResultStatus.Failed, result.Status);
        Assert.Contains("could not be started", result.Message);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

    private static ProcessExecutionResult Success(string standardOutput = "") =>
        new(0, standardOutput, string.Empty);

    private static ProcessExecutionResult Failure(string standardError = "") =>
        new(1, string.Empty, standardError);

    private static StubUpdateEnvironment CreateMacEnvironment()
    {
        var environment = new StubUpdateEnvironment
        {
            IsMacOS = true,
            ProcessPath = "/Applications/CSharpGit.app/Contents/MacOS/CSharpGit"
        };
        environment.Files.Add("/opt/homebrew/bin/brew");
        environment.ExecutableFiles.Add("/opt/homebrew/bin/brew");
        return environment;
    }

    private sealed class StubVersionProvider : IApplicationVersionProvider
    {
        public StubVersionProvider(ReleaseVersion? version)
        {
            ReleaseVersion = version;
            DisplayVersion = version?.ToString() ?? "Unknown";
        }

        public string DisplayVersion { get; }

        public ReleaseVersion? ReleaseVersion { get; }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private sealed class StubUpdateEnvironment : IApplicationUpdateEnvironment
    {
        public bool IsMacOS { get; set; }

        public string? ProcessPath { get; set; }

        public string? PathValue { get; set; }

        public char PathSeparator => ':';

        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ExecutableFiles { get; } = new(StringComparer.Ordinal);

        public bool FileExists(string path) => Files.Contains(path);

        public bool IsExecutableFile(string path) => ExecutableFiles.Contains(path);
    }

    private sealed class QueueProcessExecutor : IProcessExecutor
    {
        private readonly Queue<ProcessExecutionResult> _results;

        public QueueProcessExecutor(params ProcessExecutionResult[] results) =>
            _results = new Queue<ProcessExecutionResult>(results);

        public List<ProcessCall> Calls { get; } = [];

        public Task<ProcessExecutionResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new ProcessCall(
                executable,
                arguments.ToArray(),
                cancellationToken.CanBeCanceled));

            if (_results.Count == 0)
                throw new InvalidOperationException("No fake process result was configured.");

            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed record ProcessCall(
        string Executable,
        string[] Arguments,
        bool CanBeCanceled);
}
