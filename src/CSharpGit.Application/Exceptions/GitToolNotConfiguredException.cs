namespace CSharpGit.Application.Exceptions;

public sealed class GitToolNotConfiguredException(string toolName)
    : InvalidOperationException($"{toolName} is not configured.");
