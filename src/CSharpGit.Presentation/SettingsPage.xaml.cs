using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel = new();
    private bool _selectionReady;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += (_, _) => _selectionReady = true;
    }

    private async void CommitTimeModeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selectionReady || CommitTimeModeList.SelectedItem is not CommitTimeModeOption option) return;

        SettingsError.IsOpen = false;
        try
        {
            await _viewModel.ApplyCommitTimeModeAsync(option);
        }
        catch (Exception exception)
        {
            SettingsError.Message = exception.Message;
            SettingsError.IsOpen = true;
        }
    }
}
