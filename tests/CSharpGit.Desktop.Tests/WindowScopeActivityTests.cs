using CSharpGit.Presentation;
using Microsoft.Extensions.DependencyInjection;

namespace CSharpGit.Desktop.Tests;

public sealed class WindowScopeActivityTests
{
    [Fact]
    public void UpdateAdmissionIsIdempotent()
    {
        var activity = new ApplicationUpdateActivityState();
        Assert.False(activity.IsRunning);
        Assert.True(activity.TryBegin());
        Assert.False(activity.TryBegin());
        Assert.True(activity.IsRunning);
        activity.End();
        activity.End();
        Assert.False(activity.IsRunning);
        Assert.True(activity.TryBegin());
    }

    [Fact]
    public void SeparateWindowScopesHaveIndependentCloseSafetyStates()
    {
        using var provider = new ServiceCollection()
            .AddScoped<ApplicationUpdateActivityState>()
            .BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var left = first.ServiceProvider.GetRequiredService<ApplicationUpdateActivityState>();
        var right = second.ServiceProvider.GetRequiredService<ApplicationUpdateActivityState>();
        Assert.NotSame(left, right);
        Assert.True(left.TryBegin());
        Assert.True(left.IsRunning);
        Assert.False(right.IsRunning);
        Assert.True(right.TryBegin());
        left.End();
        Assert.True(right.IsRunning);
    }

    [Fact]
    public void WindowFactoryUsesTheScopeForFeatureModelDisposal()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "App.xaml.cs"));
        var factory = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Windowing", "WindowSessionFactory.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        Assert.Contains("repository.UseScopeOwnedChildren();", factory);
        Assert.Contains("scope.Dispose();", factory);
        Assert.Contains("Volatile.Read(ref _scopeOwnsChildren) == 0", viewModel);
        Assert.Contains("services.AddScoped<OpenRepositoryViewModel>();", app);
        Assert.DoesNotContain("page.DesktopCloneForCheck", factory);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
