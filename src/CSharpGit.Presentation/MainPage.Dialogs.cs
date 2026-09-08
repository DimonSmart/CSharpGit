using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private ContentDialog GitOperationsDialog => GetPageDialog("GitOperationsDialog");
    private ContentDialog AppearanceDialog => GetPageDialog("AppearanceDialog");

    private ContentDialog GetPageDialog(string key)
    {
        if (Resources[key] is not ContentDialog dialog)
            throw new InvalidOperationException($"Page dialog resource '{key}' was not found.");

        dialog.DataContext = _viewModel;
        dialog.XamlRoot = XamlRoot;
        return dialog;
    }
}
