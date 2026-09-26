using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private ContentDialog GitOperationsDialog => GetPageDialog("GitOperationsDialog");

    private async Task ShowInteractiveRebaseEditorAsync()
    {
        var dialog = GitOperationsDialog;

        void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            if (FindNamedDescendant<FrameworkElement>(sender, "InteractiveRebaseSection") is { } section)
                section.StartBringIntoView();

            if (FindNamedDescendant<ListView>(sender, "InteractiveRebasePlanList") is { } plan)
                plan.Focus(FocusState.Programmatic);
        }

        dialog.Opened += Dialog_Opened;
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            dialog.Opened -= Dialog_Opened;
        }
    }

    private static T? FindNamedDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        if (root is T element && string.Equals(element.Name, name, StringComparison.Ordinal))
            return element;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            if (FindNamedDescendant<T>(VisualTreeHelper.GetChild(root, index), name) is { } child)
                return child;
        }

        return null;
    }

    private ContentDialog GetPageDialog(string key)
    {
        if (Resources[key] is not ContentDialog dialog)
            throw new InvalidOperationException($"Page dialog resource '{key}' was not found.");

        dialog.DataContext = _viewModel;
        dialog.XamlRoot = XamlRoot;
        return dialog;
    }
}
