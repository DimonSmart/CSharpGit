using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Controls;

public static class WorktreeContextMenuBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorktreeContextMenuBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject dependencyObject) =>
        (bool)dependencyObject.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject dependencyObject, bool value) =>
        dependencyObject.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement element) return;

        element.RightTapped -= Element_RightTapped;
        if (args.NewValue is true) element.RightTapped += Element_RightTapped;
    }

    private static void Element_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (sender is not FrameworkElement element
            || element.DataContext is not RepositoryTreeNode
            {
                Kind: RepositoryTreeNodeKind.Worktree,
                Value: WorktreeInfo worktree
            })
            return;

        if (FindMainPage(element) is not { } page) return;
        page.ShowWorktreeContextMenu(element, worktree, args);
    }

    private static MainPage? FindMainPage(DependencyObject start)
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is MainPage page) return page;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
