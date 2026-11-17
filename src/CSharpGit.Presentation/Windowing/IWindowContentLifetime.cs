namespace CSharpGit.Presentation.Windowing;

// Phase 1 bridge to legacy MainPage cleanup; remove in migration phase 3.
internal interface IWindowContentLifetime
{
    Task PrepareForShutdownAsync(CancellationToken cancellationToken);
    void CompleteShutdown();
}
