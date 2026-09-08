namespace CSharpGit.Application.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ApplicationAssemblyDoesNotReferencePresentationOrGitIntegration()
    {
        var references = typeof(CSharpGit.Application.Abstractions.IRepositoryService).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name is "CSharpGit.Presentation" or "CSharpGit.Git");
    }
}
