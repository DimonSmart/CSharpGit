namespace CSharpGit.Presentation.Controls.CommitGraph;

/// <summary>Per-history-view graph layout and shared geometry cache, never process-global.</summary>
internal interface ICommitGraphLayoutProvider
{
    CommitGraphLayoutState Layout { get; }
    event EventHandler? LayoutChanged;
}
