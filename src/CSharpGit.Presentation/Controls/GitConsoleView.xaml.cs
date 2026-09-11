using System.Collections.ObjectModel;
using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation.Controls;

public sealed partial class GitConsoleView : UserControl
{
    private readonly ObservableCollection<GitCommandConsoleItem> _items = [];
    private bool _updatingFilter;

    public GitConsoleView()
    {
        InitializeComponent();
        CommandList.ItemsSource = _items;
        FilterComboBox.ItemsSource = new[] { "User commands", "All commands" };
        FilterComboBox.SelectedIndex = 0;
        UpdateEmptyState();
        ShowDetails(null);
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? FilterChanged;

    public GitCommandFilter Filter => FilterComboBox.SelectedIndex == 1
        ? GitCommandFilter.AllCommands
        : GitCommandFilter.UserCommands;

    public GitCommandActivity? SelectedActivity =>
        (CommandList.SelectedItem as GitCommandConsoleItem)?.Activity;

    public void SetFilter(GitCommandFilter filter)
    {
        var index = filter == GitCommandFilter.AllCommands ? 1 : 0;
        if (FilterComboBox.SelectedIndex == index) return;
        _updatingFilter = true;
        FilterComboBox.SelectedIndex = index;
        _updatingFilter = false;
    }

    public void SetActivities(IReadOnlyList<GitCommandActivity> activities, Guid? preferredSelection = null)
    {
        var selectedId = preferredSelection ?? SelectedActivity?.Id;
        _items.Clear();
        foreach (var activity in activities)
            _items.Add(new GitCommandConsoleItem(activity));

        CommandList.SelectedItem = selectedId is { } id
            ? _items.FirstOrDefault(item => item.Activity.Id == id)
            : _items.FirstOrDefault();
        UpdateEmptyState();
        ShowDetails(SelectedActivity);
    }

    public bool SelectActivity(Guid id)
    {
        var item = _items.FirstOrDefault(candidate => candidate.Activity.Id == id);
        if (item is null) return false;
        CommandList.SelectedItem = item;
        CommandList.ScrollIntoView(item);
        return true;
    }

    private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFilter) return;
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowDetails(SelectedActivity);

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedActivity is { } activity)
            CopyText(activity.DisplayCommand);
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedActivity is not { } activity) return;
        var exitCode = activity.ExitCode?.ToString() ?? string.Empty;
        var text =
            $"Command: {activity.DisplayCommand}{Environment.NewLine}" +
            $"Working directory: {activity.WorkingDirectory}{Environment.NewLine}" +
            $"Exit code: {exitCode}{Environment.NewLine}" +
            $"Duration: {FormatDuration(activity.Duration)}{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{activity.StandardOutput}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{activity.StandardError}";
        CopyText(text);
    }

    private void ShowDetails(GitCommandActivity? activity)
    {
        var hasActivity = activity is not null;
        CommandText.Text = activity?.DisplayCommand ?? string.Empty;
        WorkingDirectoryText.Text = activity?.WorkingDirectory ?? string.Empty;
        StartedText.Text = activity is null ? string.Empty : activity.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        DurationText.Text = activity is null ? string.Empty : FormatDuration(activity.Duration);
        ExitCodeText.Text = activity?.ExitCode?.ToString() ?? (activity?.Status == GitCommandStatus.Running ? "running" : string.Empty);
        StandardOutputText.Text = activity?.StandardOutput ?? string.Empty;
        StandardErrorText.Text = activity?.StandardError ?? string.Empty;
        StandardOutputText.IsEnabled = hasActivity;
        StandardErrorText.IsEnabled = hasActivity;
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CommandList.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMilliseconds < 1000)
            return $"{Math.Max(0, duration.TotalMilliseconds):0} ms";
        if (duration.TotalSeconds < 60)
            return $"{duration.TotalSeconds:0.##} s";

        var totalSeconds = Math.Max(0, (int)Math.Round(duration.TotalSeconds));
        return $"{totalSeconds / 60}m {totalSeconds % 60}s";
    }

    private sealed record GitCommandConsoleItem(GitCommandActivity Activity)
    {
        public string StartedText => Activity.StartedAt.ToLocalTime().ToString("HH:mm:ss");

        public string StatusGlyph => Activity.Status switch
        {
            GitCommandStatus.Running => "◌",
            GitCommandStatus.Succeeded => "✓",
            GitCommandStatus.Failed => "✕",
            GitCommandStatus.Cancelled => "○",
            _ => string.Empty
        };

        public string CommandText => Activity.DisplayCommand;

        public string DurationText => Activity.Status switch
        {
            GitCommandStatus.Running => "running…",
            GitCommandStatus.Cancelled => "cancelled",
            _ => FormatDuration(Activity.Duration)
        };
    }
}
