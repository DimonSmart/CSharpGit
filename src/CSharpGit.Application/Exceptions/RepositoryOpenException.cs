namespace CSharpGit.Application.Exceptions;

public class RepositoryOpenException(string message, Exception? innerException = null)
    : Exception(message, innerException);
