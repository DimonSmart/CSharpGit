namespace CSharpGit.Application.Tests;

public sealed class WindowLifetimeOwnershipContractTests
{
    [Fact]
    public void SessionUsesSingleExplicitShutdownPipelineWithoutLegacyContentInterface()
    {
        var root = FindRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var session = File.ReadAllText(Path.Combine(presentation, "Windowing", "WindowSession.cs"));
        var factory = File.ReadAllText(Path.Combine(presentation, "Windowing", "WindowSessionFactory.cs"));
        var page = File.ReadAllText(Path.Combine(presentation, "MainPage.Lifecycle.cs"));

        Assert.False(File.Exists(Path.Combine(presentation, "Windowing", "IWindowContentLifetime.cs")));
        Assert.DoesNotContain("IWindowContentLifetime", session + factory + page, StringComparison.Ordinal);
        Assert.Contains("_lifetime.PrepareAsync(prepareContent, cancellationToken)", session);
        Assert.Contains("if (!_lifetime.TryComplete()) return;", session);
        Assert.Contains("try { completeContent(); }", session);
        Assert.Contains("finally { scope.Dispose(); }", session);
        Assert.Contains("_ => page.PrepareForShutdown()", factory);
        Assert.Contains("page.BeginShutdown,", factory);
        Assert.Contains("Interlocked.Exchange(ref _shutdownFinalized, 1)", page);
    }

    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "CSharpGit.slnx")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("CSharpGit root was not found.");
    }
}
