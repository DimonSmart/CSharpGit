using CSharpGit.Application.Abstractions;
using CSharpGit.Infrastructure;

namespace CSharpGit.Presentation;

internal static class RepositoryImageServices
{
    internal static IRepositoryImageService Current { get; } = new RepositoryImageService();
}
