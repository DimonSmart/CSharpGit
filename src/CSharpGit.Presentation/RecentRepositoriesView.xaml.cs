using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class RecentRepositoriesView : UserControl
{
    public RecentRepositoriesView()
    {
        InitializeComponent();
        Loaded += RecentRepositoriesView_Loaded;
    }

    private void RecentRepositoriesView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecentRepositoriesViewModel viewModel)
            viewModel.StartImageLoading();
    }

    private void RepositoryImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentRepositoryItem item })
            item.SetRepositoryImagePath(null);
    }
}
