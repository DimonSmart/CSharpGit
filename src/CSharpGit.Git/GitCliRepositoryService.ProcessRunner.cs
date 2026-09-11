namespace CSharpGit.Git;

public sealed partial class GitCliRepositoryService
{
    private GitProcessRunner? _sharedProcessRunner;

    private GitProcessRunner SharedProcessRunner =>
        _sharedProcessRunner ??= new GitProcessRunner(_gitExecutable);
}
