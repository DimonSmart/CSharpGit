using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;

namespace CSharpGit.Presentation;

internal static class AppSettingsContext
{
    public static IAppSettingsService Current { get; } = new JsonAppSettingsService();
}
