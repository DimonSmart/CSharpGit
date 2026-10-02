namespace CSharpGit.Application.Exceptions;

public enum RepositoryCloneFailureKind
{
    InvalidTargetPath,
    TargetIsFile,
    TargetNotEmpty,
    TargetCannotBeCreated,
    GitUnavailable,
    AccessDenied,
    GitFailed
}

public sealed class RepositoryCloneException : Exception
{
    public RepositoryCloneException(
        RepositoryCloneFailureKind kind,
        string message,
        int? gitExitCode = null,
        string? gitDiagnostic = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        GitExitCode = gitExitCode;
        GitDiagnostic = gitDiagnostic;
    }

    public RepositoryCloneFailureKind Kind { get; }
    public int? GitExitCode { get; }
    public string? GitDiagnostic { get; }
}
