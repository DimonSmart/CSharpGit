namespace CSharpGit.Application.Tests;

public sealed class HistoryDiagnosticsOwnershipContractTests
{
    [Fact]
    public void HistoryViewOwnsPerformanceSetupAndShutdownWithoutMainPageForwarders()
    {
        var root = FindRepositoryRoot();
        var p = Path.Combine(root, "src", "CSharpGit.Presentation");
        var composition = File.ReadAllText(Path.Combine(p, "Composition/WindowWorkspaceComposition.Repository.cs"));
        var shutdown = File.ReadAllText(Path.Combine(p, "Composition/WindowWorkspaceComposition.Lifetime.cs"));
        var history = File.ReadAllText(Path.Combine(p, "Views", "HistoryView.Performance.cs"));

        Assert.False(File.Exists(Path.Combine(p, "MainPage.HistoryPerformanceDiagnostics.cs")));
        Assert.Contains("HistoryView.ConfigurePerformance(", composition);
        Assert.Contains("HistoryView.StopHistoryPerformanceCaptureOnShutdown()", shutdown);
        Assert.Contains("settings.Changed += PerformanceSettings_Changed", history);
        Assert.Contains("_perfSettings.Changed -= PerformanceSettings_Changed", history);
        Assert.Contains("repository.PropertyChanged += PerformanceRepository_PropertyChanged", history);
        Assert.Contains("_repository.PropertyChanged -= PerformanceRepository_PropertyChanged", history);
        Assert.Contains("StopHistoryPerformanceTimers()", history);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
