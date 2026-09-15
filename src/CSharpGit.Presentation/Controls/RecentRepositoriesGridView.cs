using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Controls;

public sealed class RecentRepositoriesGridView : GridView
{
    private readonly Dictionary<RecentRepositoryItem, GridViewItem> _containers = [];

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);

        if (element is not GridViewItem container || item is not RecentRepositoryItem repository)
            return;

        repository.PropertyChanged -= Repository_PropertyChanged;
        repository.PropertyChanged += Repository_PropertyChanged;
        _containers[repository] = container;
        ApplyColumnSpan(container, repository.RepositoryColumnSpan);
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (item is RecentRepositoryItem repository)
        {
            repository.PropertyChanged -= Repository_PropertyChanged;
            if (_containers.TryGetValue(repository, out var current)
                && ReferenceEquals(current, element))
                _containers.Remove(repository);
        }

        if (element is GridViewItem container)
            ApplyColumnSpan(container, 1);

        base.ClearContainerForItemOverride(element, item);
    }

    private void Repository_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecentRepositoryItem.RepositoryColumnSpan)
            || sender is not RecentRepositoryItem repository
            || !_containers.TryGetValue(repository, out var container))
            return;

        ApplyColumnSpan(container, repository.RepositoryColumnSpan);
        container.InvalidateMeasure();
        InvalidateMeasure();
    }

    private static void ApplyColumnSpan(GridViewItem container, int span) =>
        VariableSizedWrapGrid.SetColumnSpan(container, span);
}
