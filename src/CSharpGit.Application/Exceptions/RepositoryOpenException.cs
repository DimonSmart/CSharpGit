namespace CSharpGit.Application.Exceptions;

public sealed class RepositoryOpenException(string message, Exception? innerException = null)
    : Exception(message, innerException);
