using System.Reflection;
using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;
using CSharpGit.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CSharpGit.Application.Tests;

public sealed class SettingsFacetContractsTests
{
    [Fact]
    public void AggregateRetainsEachSettingsCapabilityWithoutBroadeningFacets()
    {
        var facets = new[]
        {
            typeof(IAppearanceSettings), typeof(IGitWorkflowSettings),
            typeof(IDiagnosticsSettings), typeof(IRepositoryLauncherSettings)
        };
        foreach (var facet in facets)
        {
            Assert.True(facet.IsAssignableTo(typeof(IAppSettingsChangeSource)));
            Assert.True(facet.IsAssignableFrom(typeof(IAppSettingsService)));
        }

        Assert.Empty(typeof(IAppSettingsService).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Empty(typeof(IAppSettingsService).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal(
            new[] { "CommitTimeDisplayMode", "HistoryRenderingMode", "OnlineAvatarLookupEnabled", "ShowAuthorAvatars", "ShowReflog", "ThemeMode" },
            typeof(IAppearanceSettings).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            new[] { "AutoSetupRemoteOnPush", "DefaultPullStrategy", "ForcePullAutoStash" },
            typeof(IGitWorkflowSettings).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            new[] { "GitConsoleAutoOpenMode", "HistoryPerformanceDiagnosticsEnabled", "LoggingEnabled", "LogLevel" },
            typeof(IDiagnosticsSettings).GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(5, typeof(IRepositoryLauncherSettings).GetProperties().Length);
        Assert.DoesNotContain(typeof(IGitWorkflowSettings).GetMethods(), method => method.Name.Contains("Reflog", StringComparison.Ordinal));
    }

    [Fact]
    public void FeatureConstructorsRequireOnlyTheirOwnSettingsFacet()
    {
        Assert.Equal(typeof(IGitWorkflowSettings), SettingsParameter<RepositorySyncViewModel>());
        Assert.Equal(typeof(IAppearanceSettings), SettingsParameter<HistoryViewModel>());
        Assert.Equal(typeof(IAppSettingsService), SettingsParameter<SettingsViewModel>());
    }

    [Fact]
    public async Task FacetsShareOnePersistedStateAndOneEventPublisher()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CSharpGitSettingsFacets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new JsonAppSettingsService(Path.Combine(directory, "settings.json"));
            var services = new ServiceCollection();
            services.AddSingleton<IAppSettingsService>(settings);
            services.AddSingleton<IAppSettingsChangeSource>(provider => provider.GetRequiredService<IAppSettingsService>());
            services.AddSingleton<IAppearanceSettings>(provider => provider.GetRequiredService<IAppSettingsService>());
            services.AddSingleton<IGitWorkflowSettings>(provider => provider.GetRequiredService<IAppSettingsService>());
            services.AddSingleton<IDiagnosticsSettings>(provider => provider.GetRequiredService<IAppSettingsService>());
            services.AddSingleton<IRepositoryLauncherSettings>(provider => provider.GetRequiredService<IAppSettingsService>());
            using var container = services.BuildServiceProvider();

            var appearance = container.GetRequiredService<IAppearanceSettings>();
            var workflow = container.GetRequiredService<IGitWorkflowSettings>();
            var diagnostics = container.GetRequiredService<IDiagnosticsSettings>();
            var launcher = container.GetRequiredService<IRepositoryLauncherSettings>();
            Assert.Same(settings, appearance);
            Assert.Same(settings, workflow);
            Assert.Same(settings, diagnostics);
            Assert.Same(settings, launcher);
            Assert.Same(settings, container.GetRequiredService<IAppSettingsChangeSource>());

            var notifications = 0;
            appearance.Changed += (_, _) =>
            {
                Assert.True(workflow.AutoSetupRemoteOnPush);
                Interlocked.Increment(ref notifications);
            };
            await workflow.SetAutoSetupRemoteOnPushAsync(true);
            await workflow.SetAutoSetupRemoteOnPushAsync(true);
            Assert.Equal(1, notifications);
            await launcher.SetRecentRepositoriesExpandedAsync(false);
            Assert.False(launcher.RecentRepositoriesExpanded);
            Assert.True(workflow.AutoSetupRemoteOnPush);
            Assert.Equal(settings.ThemeMode, appearance.ThemeMode);
            Assert.Equal(settings.LoggingEnabled, diagnostics.LoggingEnabled);
            Assert.True(new JsonAppSettingsService(Path.Combine(directory, "settings.json")).AutoSetupRemoteOnPush);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentChangesViaDifferentFacetsPreserveStateAndPublishCommittedValues()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CSharpGitFacetConcurrency", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsFile = Path.Combine(directory, "settings.json");
            var settings = new JsonAppSettingsService(settingsFile);
            var appearance = (IAppearanceSettings)settings;
            var workflow = (IGitWorkflowSettings)settings;
            var diagnostics = (IDiagnosticsSettings)settings;
            var launcher = (IRepositoryLauncherSettings)settings;
            var committedObservations = 0;
            var backgroundObservations = 0;
            appearance.Changed += (_, _) =>
            {
                if (diagnostics.LoggingEnabled)
                    Assert.Equal(ApplicationLogLevel.Warning, diagnostics.LogLevel);
                if (Thread.CurrentThread.IsThreadPoolThread)
                    Interlocked.Increment(ref backgroundObservations);
                Interlocked.Increment(ref committedObservations);
            };

            await Task.WhenAll(
                Task.Run(() => appearance.SetHistoryRenderingModeAsync(HistoryRenderingMode.SubjectOnly)),
                Task.Run(() => workflow.SetAutoSetupRemoteOnPushAsync(true)),
                Task.Run(() => diagnostics.SetLoggingSettingsAsync(true, ApplicationLogLevel.Warning)),
                Task.Run(() => launcher.SetRecentRepositoriesExpandedAsync(false)));

            Assert.Equal(4, committedObservations);
            Assert.True(backgroundObservations > 0);
            var restored = new JsonAppSettingsService(settingsFile);
            Assert.Equal(HistoryRenderingMode.SubjectOnly, restored.HistoryRenderingMode);
            Assert.True(restored.AutoSetupRemoteOnPush);
            Assert.True(restored.LoggingEnabled);
            Assert.Equal(ApplicationLogLevel.Warning, restored.LogLevel);
            Assert.False(restored.RecentRepositoriesExpanded);

            await diagnostics.SetLoggingSettingsAsync(true, ApplicationLogLevel.Warning);
            Assert.Equal(4, committedObservations);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Type SettingsParameter<T>() =>
        typeof(T).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Single(parameter => parameter.Name == "settings").ParameterType;
}
