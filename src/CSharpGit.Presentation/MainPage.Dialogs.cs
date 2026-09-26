using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private ContentDialog GitOperationsDialog => GetPageDialog("GitOperationsDialog");
    private ContentDialog InteractiveRebaseDialog => GetPageDialog("InteractiveRebaseDialog");

    private async void OpenInteractiveRebase_Click(object sender, RoutedEventArgs e)
    {
        if (!await _viewModel.PrepareInteractiveRebaseAsync())
            return;

        GitOperationsDialog.Hide();
        await Task.Delay(20);
        await ShowInteractiveRebaseEditorAsync();
    }

    private async Task ShowInteractiveRebaseEditorAsync()
    {
        var dialog = InteractiveRebaseDialog;

        void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            if (FindNamedDescendant<TextBox>(sender, "InteractiveRebaseTodoEditor") is not { } editor)
                return;

            editor.Width = Math.Min(
                900,
                Math.Max(
                    240,
                    ActualWidth - 96));
            editor.Focus(FocusState.Programmatic);
            editor.Select(editor.Text.Length, 0);
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

        if (FindNamedDescendant<TextBox>(dialog, "InteractiveRebaseTodoEditor") is { } editor)
            await _viewModel.StartPreparedInteractiveRebaseAsync(editor.Text);
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
