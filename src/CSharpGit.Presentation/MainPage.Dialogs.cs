using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private ContentDialog InteractiveRebaseDialog => GetPageDialog("InteractiveRebaseDialog");



    private async Task ShowInteractiveRebaseEditorAsync()
    {
        var dialog = InteractiveRebaseDialog;

        void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            if (FindNamedDescendant<InteractiveRebaseTodoEditor>(
                    sender,
                    "InteractiveRebaseTodoEditor") is not { } editor)
                return;

            var rootSize = sender.XamlRoot?.Size;
            var viewportWidth = rootSize?.Width ?? ActualWidth;
            var viewportHeight = rootSize?.Height ?? ActualHeight;

            var availableDialogWidth = Math.Max(320d, viewportWidth - 96d);
            var dialogWidth = Math.Min(1200d, availableDialogWidth);
            dialog.MaxWidth = dialogWidth;
            dialog.MinWidth = Math.Min(900d, dialogWidth);
            editor.Width = Math.Max(240d, dialogWidth - 48d);

            var availableEditorHeight = Math.Max(220d, viewportHeight - 220d);
            editor.MinHeight = Math.Min(350d, availableEditorHeight);
            editor.MaxHeight = Math.Min(
                650d,
                Math.Max(editor.MinHeight, availableEditorHeight));
            editor.Height = Math.Min(600d, editor.MaxHeight);
            editor.FocusEditor();
        }

        dialog.Opened += Dialog_Opened;
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            dialog.Opened -= Dialog_Opened;
        }

        if (result != ContentDialogResult.Primary)
            return;

        if (FindNamedDescendant<InteractiveRebaseTodoEditor>(
                dialog,
                "InteractiveRebaseTodoEditor") is { } editor)
        {
            await _viewModel.StartPreparedInteractiveRebaseAsync(editor.Text);
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
