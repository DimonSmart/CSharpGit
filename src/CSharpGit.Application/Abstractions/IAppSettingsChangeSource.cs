namespace CSharpGit.Application.Abstractions;

/// <summary>
/// Raised after successful persistence and publication of a settings change.
/// Callbacks may run on any thread and read the current (possibly newer) state.
/// A no-op need not raise an event.
/// </summary>
public interface IAppSettingsChangeSource
{
    event EventHandler? Changed;
}
