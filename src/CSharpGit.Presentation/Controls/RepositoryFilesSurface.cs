using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Controls;

/// <summary>
/// Owns the dynamically composed repository-files tab. MainPage handles user
/// actions, while this control owns its visual tree and preview split layout.
/// </summary>
internal sealed class RepositoryFilesSurface : Grid
{
    internal TextBox SearchBox { get; }
    internal ComboBox SearchMode { get; }
    internal TreeView FileTree { get; }
    internal ListView ContentResults { get; }
    internal ProgressRing Progress { get; }
    internal TextBlock Status { get; }
    internal FilePreviewHost PreviewHost { get; }

    internal RepositoryFilesSurface(RepositoryFilesViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        MinHeight = 120;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Grid
        {
            Padding = new Thickness(8, 4, 8, 4),
            ColumnSpacing = 8
        };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        SearchBox = new TextBox
        {
            Style = UiStyles.Resolve<Style>("CompactTextBoxStyle"),
            PlaceholderText = "Search files...",
            MinWidth = 180
        };
        toolbar.Children.Add(SearchBox);

        SearchMode = new ComboBox
        {
            Style = UiStyles.Resolve<Style>("CompactComboBoxStyle"),
            MinWidth = 110,
            ItemsSource = new[]
            {
                RepositoryFilesViewModel.NameSearchMode,
                RepositoryFilesViewModel.ContentSearchMode
            },
            SelectedIndex = 0
        };
        Grid.SetColumn(SearchMode, 1);
        toolbar.Children.Add(SearchMode);
        Children.Add(toolbar);

        var leftPane = new Grid();
        FileTree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["DenseTreeViewStyle"],
            ItemContainerStyle = (Style)Microsoft.UI.Xaml.Application.Current.Resources["DenseTreeItemStyle"],
            ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources["RepositoryFilesTreeItemTemplate"],
            ItemsSource = viewModel.TreeRoots
        };
        leftPane.Children.Add(FileTree);

        ContentResults = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            Visibility = Visibility.Collapsed,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        leftPane.Children.Add(ContentResults);

        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        Progress = new ProgressRing
        {
            Width = 18,
            Height = 18,
            IsActive = false,
            Visibility = Visibility.Collapsed
        };
        Status = new TextBlock
        {
            Text = string.Empty,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
            TextAlignment = TextAlignment.Center
        };
        statusPanel.Children.Add(Progress);
        statusPanel.Children.Add(Status);
        leftPane.Children.Add(statusPanel);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(2, GridUnitType.Star),
            MinWidth = 180
        });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        body.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(3, GridUnitType.Star),
            MinWidth = 250
        });

        Grid.SetColumn(leftPane, 0);
        body.Children.Add(leftPane);

        var splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            MinimumFirst = 180,
            MinimumSecond = 250,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);

        PreviewHost = new FilePreviewHost();
        Grid.SetColumn(PreviewHost, 2);
        body.Children.Add(PreviewHost);
        Grid.SetRow(body, 1);
        Children.Add(body);
    }

    internal void UpdateStatus(string text, bool loading)
    {
        Status.Text = text;
        Progress.IsActive = loading;
        Progress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }
}
