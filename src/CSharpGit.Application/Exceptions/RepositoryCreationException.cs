namespace CSharpGit.Application.Exceptions;

public enum RepositoryCreationFailureKind
{
    InvalidPath,
    TargetIsFile,
    TargetCannotBeCreated,
    RepositoryAlreadyExists,
    BareTargetNotEmpty,
    GitUnavailable,
    GitFailed,
    AccessDenied
}

public sealed class RepositoryCreationException : Exception
{
    public RepositoryCreationException(
        RepositoryCreationFailureKind kind,
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

    public RepositoryCreationFailureKind Kind { get; }
    public int? GitExitCode { get; }
    public string? GitDiagnostic { get; }
}
