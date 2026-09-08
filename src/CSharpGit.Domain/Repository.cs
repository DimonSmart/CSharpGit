namespace CSharpGit.Domain;

public sealed record Repository(
    string WorkingDirectory,
    string RepositoryRoot,
    string GitDirectory,
    bool IsWorktree);
