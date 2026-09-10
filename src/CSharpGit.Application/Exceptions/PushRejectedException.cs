namespace CSharpGit.Application.Exceptions;

public enum PushResultKind
{
    Success,
    NonFastForwardRejected,
    LeaseRejected,
    RemoteRejected,
    AuthenticationOrTransportFailure,
    OtherFailure
}

public sealed class PushRejectedException(
    PushResultKind resultKind,
    string message,
    Exception? innerException = null)
    : RepositoryOpenException(message, innerException)
{
    public PushResultKind ResultKind { get; } = resultKind;
}

public enum ForcePushPreparationFailure
{
    MissingUpstream,
    RemoteBranchDoesNotExist,
    MultiplePushDestinations
}

public sealed class ForcePushWithLeasePreparationException(
    ForcePushPreparationFailure failure,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public ForcePushPreparationFailure Failure { get; } = failure;
}

public sealed class ForcePushWithLeaseCancelledException(string message)
    : Exception(message);
