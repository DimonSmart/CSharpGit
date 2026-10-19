using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation.Controls;

/// <summary>Presentation-only reusable surface; never owns a Git process.</summary>
public sealed partial class GitCommandFeedbackView : UserControl
{
    private GitCommandFeedbackSnapshot? _current;
    private bool _hovered;
    private bool _focused;

    public GitCommandFeedbackView()
    {
        InitializeComponent();
    }

    internal event Action<Guid>? DismissRequested;
    internal event Action<Guid>? OpenConsoleRequested;
    internal event Action<bool, bool>? InteractionChanged;

    internal void Apply(GitCommandFeedbackSnapshot state)
    {
        if (_current == state) return;
        var changedActivity = _current?.ActivityId != state.ActivityId;
        var changedStatus = _current?.Status != state.Status;
        _current = state;

        if (changedStatus)
            StatusText.Style = UiStyles.Resolve<Style>(
                state.Status == GitCommandStatus.Succeeded ? "SuccessStatusTextStyle" : "BodyTextStyle");
        StatusText.Text = state.StatusText;
        RunningRing.IsActive = state.Status == GitCommandStatus.Running;
        RunningRing.Visibility = RunningRing.IsActive ? Visibility.Visible : Visibility.Collapsed;
        if (changedActivity || CommandText.Text != state.DisplayCommand)
        {
            CommandText.Text = state.DisplayCommand;
            ToolTipService.SetToolTip(CommandText, state.DisplayCommand);
            HintText.Visibility = Visibility.Collapsed;
        }

        // Do not reset an actively selected preview or its scroll position on
        // each progress tick. New data is displayed when focus is released.
        if (changedActivity || !_focused)
        {
            if (OutputText.Text != state.PreviewText)
                OutputText.Text = state.PreviewText;
        }

        ResultDetails.Text = GitCommandConsoleItem.FormatDuration(state.Duration);
        if (state.Status == GitCommandStatus.Failed && state.ExitCode is { } exitCode)
            ResultDetails.Text += " · exit " + exitCode;
        DismissButton.Content = state.Status == GitCommandStatus.Running ? "Hide" : "Close";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            DismissButton, state.Status == GitCommandStatus.Running ? "Hide" : "Close");
    }

    internal void ShowUnavailableMessage()
    {
        HintText.Text = "This command is no longer available in Git Console history.";
        HintText.Visibility = Visibility.Visible;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { } state) return;
        var package = new DataPackage();
        package.SetText(state.DisplayCommand);
        Clipboard.SetContent(package);
    }

    private void OpenConsole_Click(object sender, RoutedEventArgs e)
    {
        if (_current is { } state)
            OpenConsoleRequested?.Invoke(state.ActivityId);
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_current is { } state)
            DismissRequested?.Invoke(state.ActivityId);
    }

    private void Surface_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _hovered = true;
        InteractionChanged?.Invoke(_hovered, _focused);
    }

    private void Surface_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _hovered = false;
        InteractionChanged?.Invoke(_hovered, _focused);
    }

    private void Surface_GotFocus(object sender, RoutedEventArgs e)
    {
        _focused = true;
        InteractionChanged?.Invoke(_hovered, _focused);
    }

    private void Surface_LostFocus(object sender, RoutedEventArgs e)
    {
        _focused = false;
        InteractionChanged?.Invoke(_hovered, _focused);
        if (_current is { } state && OutputText.Text != state.PreviewText)
            OutputText.Text = state.PreviewText;
    }
}
