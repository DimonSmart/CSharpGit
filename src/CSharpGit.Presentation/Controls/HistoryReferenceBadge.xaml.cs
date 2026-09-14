using System.Collections.Specialized;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Controls;

public sealed partial class HistoryReferenceBadge : UserControl
{
    public static readonly DependencyProperty ReferenceNameProperty = DependencyProperty.Register(
        nameof(ReferenceName),
        typeof(string),
        typeof(HistoryReferenceBadge),
        new PropertyMetadata(string.Empty, OnReferenceNameChanged));

    private OpenRepositoryViewModel? _viewModel;

    public HistoryReferenceBadge()
    {
        InitializeComponent();
        Loaded += HistoryReferenceBadge_Loaded;
        Unloaded += HistoryReferenceBadge_Unloaded;
    }

    public string ReferenceName
    {
        get => (string?)GetValue(ReferenceNameProperty) ?? string.Empty;
        set => SetValue(ReferenceNameProperty, value);
    }

    private static void OnReferenceNameChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((HistoryReferenceBadge)dependencyObject).UpdatePresentation();

    private void HistoryReferenceBadge_Loaded(object sender, RoutedEventArgs args)
    {
        AttachViewModel();
        UpdatePresentation();
    }

    private void HistoryReferenceBadge_Unloaded(object sender, RoutedEventArgs args) => DetachViewModel();

    private void AttachViewModel()
    {
        DetachViewModel();
        _viewModel = FindViewModel();
        if (_viewModel is not null)
            _viewModel.RemoteBranches.CollectionChanged += RemoteBranches_CollectionChanged;
    }

    private void DetachViewModel()
    {
        if (_viewModel is not null)
            _viewModel.RemoteBranches.CollectionChanged -= RemoteBranches_CollectionChanged;
        _viewModel = null;
    }

    private void RemoteBranches_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        UpdatePresentation();

    private OpenRepositoryViewModel? FindViewModel()
    {
        for (DependencyObject? current = this; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: OpenRepositoryViewModel viewModel })
                return viewModel;
        }

        return null;
    }

    private void UpdatePresentation()
    {
        ReferenceText.Text = ReferenceName;
        var isDefaultRemoteBranch = _viewModel?.RemoteBranches.Any(branch =>
            branch.IsDefault && string.Equals(branch.Name, ReferenceName, StringComparison.Ordinal)) == true;
        DefaultBranchIcon.Visibility = isDefaultRemoteBranch ? Visibility.Visible : Visibility.Collapsed;
    }
}
