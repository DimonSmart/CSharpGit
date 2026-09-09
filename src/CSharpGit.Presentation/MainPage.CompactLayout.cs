using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _compactLayoutApplied;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_compactLayoutApplied) return;

        Loaded -= ApplyCompactLayoutWhenLoaded;
        Loaded += ApplyCompactLayoutWhenLoaded;
        _ = DispatcherQueue.TryEnqueue(ApplyCompactWorkspaceLayout);
    }

    private void ApplyCompactLayoutWhenLoaded(object sender, RoutedEventArgs args)
    {
        ApplyCompactWorkspaceLayout();
    }

    private void ApplyCompactWorkspaceLayout()
    {
        if (_compactLayoutApplied || RepositoryWorkspace is null) return;

        _compactLayoutApplied = true;
        Loaded -= ApplyCompactLayoutWhenLoaded;

        ApplyWorkspaceFrameDensity();
        ApplyRepositoryDensity();
        ApplyHistoryDensity();
        ApplyDetailsDensity();
        ApplyWorkingTreeDensity();
        ApplyStatusDensity();
    }

    private void ApplyWorkspaceFrameDensity()
    {
        RepositoryWorkspace.RowDefinitions[0].Height = new GridLength(46);
        RepositoryWorkspace.RowDefinitions[3].Height = new GridLength(26);

        var body = RepositoryWorkspace.Children
            .OfType<Grid>()
            .FirstOrDefault(element => Grid.GetRow(element) == 2);
        if (body is not null && body.ColumnDefinitions.Count >= 3)
        {
            body.MinHeight = 300;
            body.ColumnDefinitions[0].Width = new GridLength(270);
            body.ColumnDefinitions[0].MinWidth = 180;
            body.ColumnDefinitions[0].MaxWidth = 430;
            body.ColumnDefinitions[1].Width = new GridLength(4);
            body.ColumnDefinitions[2].MinWidth = 520;
        }

        var toolbarBorder = RepositoryWorkspace.Children
            .OfType<Border>()
            .FirstOrDefault(element => Grid.GetRow(element) == 0);
        if (toolbarBorder?.Child is not Grid toolbar) return;

        toolbarBorder.Padding = new Thickness(8, 0);
        toolbar.ColumnSpacing = 6;

        foreach (var separator in toolbar.Children.OfType<Border>())
        {
            if (Grid.GetColumn(separator) is 1 or 3) separator.Height = 20;
        }

        if (toolbar.Children.OfType<StackPanel>().FirstOrDefault(element => Grid.GetColumn(element) == 0) is { } brand)
        {
            brand.Spacing = 6;
            foreach (var icon in brand.Children.OfType<FontIcon>()) icon.FontSize = 16;
            foreach (var text in brand.Children.OfType<TextBlock>()) text.FontSize = 15;
        }

        if (toolbar.Children.OfType<StackPanel>().FirstOrDefault(element => Grid.GetColumn(element) == 4) is { } branch)
        {
            branch.Spacing = 5;
            branch.Margin = new Thickness(2, 0, 3, 0);
            foreach (var icon in branch.Children.OfType<FontIcon>()) icon.FontSize = 13;
            foreach (var text in branch.Children.OfType<TextBlock>()) text.FontSize = 13;
        }

        if (toolbar.Children.OfType<StackPanel>().FirstOrDefault(element => Grid.GetColumn(element) == 5) is { } actions)
        {
            actions.Spacing = 2;
            foreach (var button in actions.Children.OfType<Button>())
            {
                button.Padding = new Thickness(7, 3);
                if (button.Content is not StackPanel content) continue;
                content.Spacing = 4;
                foreach (var icon in content.Children.OfType<FontIcon>()) icon.FontSize = 12;
                foreach (var text in content.Children.OfType<TextBlock>()) text.FontSize = 13;
            }

            foreach (var ring in actions.Children.OfType<ProgressRing>())
            {
                ring.Width = ring.Height = 16;
                ring.Margin = new Thickness(6, 0, 0, 0);
            }
        }

        if (toolbar.Children.OfType<Button>().FirstOrDefault(element => Grid.GetColumn(element) == 6) is { } more)
        {
            more.Width = 32;
            more.Height = 28;
            if (more.Content is FontIcon icon) icon.FontSize = 14;
        }
    }

    private void ApplyRepositoryDensity()
    {
        RepositoryTree.ItemTemplate = CompactResource<DataTemplate>("CompactRepositoryTreeItemTemplate");
        RepositoryTree.Padding = new Thickness(3, 1, 3, 3);

        if (RepositoryTree.Parent is not Grid repositoryPane) return;
        repositoryPane.RowDefinitions[0].Height = new GridLength(32);

        if (repositoryPane.Children
            .OfType<TextBlock>()
            .FirstOrDefault(element => Grid.GetRow(element) == 0) is { } title)
        {
            title.FontSize = 13;
            title.Margin = new Thickness(10, 0);
        }
    }

    private void ApplyHistoryDensity()
    {
        HistoryPane.RowDefinitions[0].Height = new GridLength(38);
        HistoryPane.RowDefinitions[1].MinHeight = 160;
        HistoryPane.RowDefinitions[2].Height = new GridLength(4);
        HistoryPane.RowDefinitions[3].Height = new GridLength(260);
        HistoryPane.RowDefinitions[3].MinHeight = 130;

        HistoryFilter.FontSize = 13;
        ScopeCombo.FontSize = 13;
        ScopeCombo.MinWidth = 145;

        if (HistoryFilter.Parent is Grid filterRow)
        {
            filterRow.ColumnSpacing = 6;
            if (filterRow.Parent is Border filterBorder)
                filterBorder.Padding = new Thickness(6, 3);
        }

        if (HistoryList.Parent is Grid historyListPane)
        {
            historyListPane.RowDefinitions[0].Height = new GridLength(24);
            if (historyListPane.Children
                .OfType<Border>()
                .FirstOrDefault(element => Grid.GetRow(element) == 0)?.Child is Grid heading)
            {
                foreach (var text in heading.Children.OfType<TextBlock>()) text.FontSize = 10;
            }
        }

        HistoryList.ItemContainerStyle = CompactResource<Style>("CompactHistoryItemContainerStyle");
        HistoryList.ItemTemplate = CompactResource<DataTemplate>("CompactHistoryItemTemplate");

        LoadMoreHistoryButton.Padding = new Thickness(9, 2);
        LoadMoreHistoryButton.Margin = new Thickness(0, 2);

        DetailsTabs.HeaderTemplate = CompactResource<DataTemplate>("CompactPivotHeaderTemplate");
    }

    private void ApplyDetailsDensity()
    {
        DetailsScroller.MinHeight = 130;
        if (DetailsScroller.Content is StackPanel details)
        {
            details.Padding = new Thickness(10, 6);
            details.Spacing = 6;
            if (details.Children.OfType<TextBlock>().FirstOrDefault() is { } message)
                message.FontSize = 13;
        }

        if (FilesTab.Content is Grid changes)
        {
            changes.MinHeight = 130;
            if (changes.ColumnDefinitions.Count >= 3)
            {
                changes.ColumnDefinitions[0].MinWidth = 210;
                changes.ColumnDefinitions[1].Width = new GridLength(4);
                changes.ColumnDefinitions[2].MinWidth = 270;
            }
        }

        ChangedFilesTree.ItemTemplate = CompactResource<DataTemplate>("CompactChangedFileTreeItemTemplate");
        ChangedFilesTree.Padding = new Thickness(2, 0, 2, 2);

        if (ChangedFilesTree.Parent is Grid changedFilesPane)
        {
            changedFilesPane.RowDefinitions[0].Height = new GridLength(28);
            if (changedFilesPane.Children
                .OfType<Grid>()
                .FirstOrDefault(element => Grid.GetRow(element) == 0) is { } heading)
            {
                heading.Padding = new Thickness(7, 0);
                SetColumns(heading, 26, 46, 46);
                foreach (var text in heading.Children.OfType<TextBlock>())
                    text.FontSize = Grid.GetColumn(text) == 0 ? 13 : 9;
            }
        }

        CompactDiffList.ItemContainerStyle = CompactResource<Style>("CompactDiffItemContainerStyle");
        CompactDiffList.ItemTemplate = CompactResource<DataTemplate>("CompactDiffItemTemplate");

        if (CompactDiffList.Parent is Grid diffContent && diffContent.Parent is Grid diffPane)
        {
            diffPane.RowDefinitions[0].Height = new GridLength(30);
            diffPane.RowDefinitions[1].Height = new GridLength(18);

            if (diffPane.Children
                .OfType<Border>()
                .FirstOrDefault(element => Grid.GetRow(element) == 0) is { } pathBorder)
            {
                pathBorder.Padding = new Thickness(7, 0);
                if (pathBorder.Child is TextBlock path) path.FontSize = 12;
            }

            if (diffPane.Children
                .OfType<Border>()
                .FirstOrDefault(element => Grid.GetRow(element) == 1)?.Child is Grid lineHeading)
            {
                if (lineHeading.ColumnDefinitions.Count >= 2)
                {
                    lineHeading.ColumnDefinitions[0].Width = new GridLength(38);
                    lineHeading.ColumnDefinitions[1].Width = new GridLength(38);
                }

                foreach (var text in lineHeading.Children.OfType<TextBlock>())
                {
                    text.FontSize = 8;
                    text.Margin = new Thickness(0, 0, 5, 0);
                }
            }
        }
    }

    private void ApplyWorkingTreeDensity()
    {
        WorkingTreePane.Padding = new Thickness(10);

        if (WorkingTreePane.Children
            .OfType<Grid>()
            .FirstOrDefault(element => Grid.GetRow(element) == 0) is not { } heading) return;

        heading.Margin = new Thickness(0, 0, 0, 8);
        if (heading.Children.OfType<StackPanel>().FirstOrDefault() is not { } copy) return;

        var text = copy.Children.OfType<TextBlock>().ToArray();
        if (text.Length > 0) text[0].FontSize = 18;
        if (text.Length > 1) text[1].FontSize = 13;
    }

    private void ApplyStatusDensity()
    {
        var statusBorder = RepositoryWorkspace.Children
            .OfType<Border>()
            .FirstOrDefault(element => Grid.GetRow(element) == 3);
        if (statusBorder?.Child is not Grid status) return;

        statusBorder.Padding = new Thickness(8, 0);
        status.ColumnSpacing = 12;

        foreach (var text in Descendants<TextBlock>(status)) text.FontSize = 12;
        foreach (var icon in Descendants<FontIcon>(status)) icon.FontSize = Math.Min(icon.FontSize, 11);
    }

    private static T CompactResource<T>(string key) where T : class
        => Application.Current.Resources[key] as T
           ?? throw new InvalidOperationException($"Compact UI resource '{key}' was not loaded.");

    private static void SetColumns(Grid grid, double second, double third, double fourth)
    {
        if (grid.ColumnDefinitions.Count < 4) return;
        grid.ColumnDefinitions[1].Width = new GridLength(second);
        grid.ColumnDefinitions[2].Width = new GridLength(third);
        grid.ColumnDefinitions[3].Width = new GridLength(fourth);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
