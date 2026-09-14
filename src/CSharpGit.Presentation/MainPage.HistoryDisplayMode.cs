using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private bool _syncingHistoryDisplayMode;

    private void HistoryDisplayModeSelector_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel.PropertyChanged += HistoryDisplayModeViewModel_PropertyChanged;
        SyncHistoryDisplayModeSelector();
    }

    private void HistoryDisplayModeSelector_Unloaded(object sender, RoutedEventArgs e) =>
        _viewModel.PropertyChanged -= HistoryDisplayModeViewModel_PropertyChanged;

    private void HistoryDisplayModeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenRepositoryViewModel.SelectedScope) or nameof(OpenRepositoryViewModel.ShowReflog))
            SyncHistoryDisplayModeSelector();
    }

    private void HistoryDisplayModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs e)
    {
        if (_syncingHistoryDisplayMode || sender.SelectedItem is null) return;

        var mode = ReferenceEquals(sender.SelectedItem, CurrentScopeItem)
            ? HistoryDisplayMode.CurrentBranch
            : ReferenceEquals(sender.SelectedItem, ShowReflogToggle)
                ? HistoryDisplayMode.AllReferencesWithReflog
                : HistoryDisplayMode.AllReferences;

        _viewModel.SetHistoryDisplayMode(mode);
    }

    private void SyncHistoryDisplayModeSelector()
    {
        var item = _viewModel.HistoryDisplayMode switch
        {
            HistoryDisplayMode.CurrentBranch => CurrentScopeItem,
            HistoryDisplayMode.AllReferencesWithReflog => ShowReflogToggle,
            _ => AllScopeItem
        };

        if (ReferenceEquals(ScopeCombo.SelectedItem, item)) return;

        _syncingHistoryDisplayMode = true;
        try
        {
            ScopeCombo.SelectedItem = item;
        }
        finally
        {
            _syncingHistoryDisplayMode = false;
        }
    }
}
