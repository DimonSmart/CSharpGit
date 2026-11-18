using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Views;

/// <summary>
/// Owns the working-tree commit editor UI. The surrounding workflow owns
/// stash dialog execution until the feature controller is extracted.
/// </summary>
public sealed partial class WorkingTreeCommitEditorView : UserControl
{
    internal event RoutedEventHandler? StashRequested;

    public WorkingTreeCommitEditorView() => InitializeComponent();

    private void Stash_Click(object sender, RoutedEventArgs args) =>
        StashRequested?.Invoke(this, args);
}
