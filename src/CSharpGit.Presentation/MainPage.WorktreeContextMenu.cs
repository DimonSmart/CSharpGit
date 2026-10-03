using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    internal void ShowWorktreeContextMenu(
        FrameworkElement source,
        WorktreeInfo worktree,
        RightTappedRoutedEventArgs args)
    {
        var flyout = new MenuFlyout();
        PopulateWorktreeMenu(flyout, worktree);
        flyout.ShowAt(source, args.GetPosition(source));
        args.Handled = true;
    }
}
