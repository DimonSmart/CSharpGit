using CSharpGit.Presentation.Previewing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Controls;

public sealed class ImageDiffPreviewHost : Grid
{
    private const double TwoColumnBreakpoint = 720;

    private readonly Grid _sides = new() { ColumnSpacing = 12, RowSpacing = 12 };
    private readonly ImagePreviewView _originalView = new();
    private readonly ImagePreviewView _changedView = new();
    private readonly Grid _originalContainer;
    private readonly Grid _changedContainer;
    private readonly StackPanel _summary = new() { Spacing = 3, Margin = new Thickness(4, 10, 4, 0) };
    private bool _twoColumns;

    public ImageDiffPreviewHost()
    {
        Margin = new Thickness(8);
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _originalContainer = BuildSide("ORIGINAL", _originalView);
        _changedContainer = BuildSide("CHANGED", _changedView);
        _sides.Children.Add(_originalContainer);
        _sides.Children.Add(_changedContainer);
        Children.Add(_sides);

        Grid.SetRow(_summary, 1);
        Children.Add(_summary);

        SizeChanged += (_, args) => UpdateResponsiveLayout(args.NewSize.Width);
        UpdateResponsiveLayout(ActualWidth);
    }

    internal void Show(ImageDiffPreviewContent content)
    {
        ShowSide(_originalView, content.Original, "Not present before this change");
        ShowSide(_changedView, content.Changed, "Deleted by this change");

        _summary.Children.Clear();
        foreach (var line in BuildSummary(content))
        {
            _summary.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Opacity = 0.78
            });
        }
    }

    internal void Clear()
    {
        _originalView.Show(null, 0, null, null, false, null, string.Empty);
        _changedView.Show(null, 0, null, null, false, null, string.Empty);
        _summary.Children.Clear();
    }

    private static Grid BuildSide(string title, ImagePreviewView view)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.72,
            Margin = new Thickness(4, 0, 4, 6)
        });
        Grid.SetRow(view, 1);
        grid.Children.Add(view);
        return grid;
    }

    private static void ShowSide(
        ImagePreviewView view,
        ImageDiffSideContent side,
        string missingMessage)
    {
        var canRender = side.State == ImageDiffSideState.Image;
        view.Show(
            side.LocalPath,
            side.FileSize,
            side.Metadata,
            side.DetectedFormat,
            canRender,
            side.UnavailableReason,
            side.State == ImageDiffSideState.Missing
                ? side.UnavailableReason ?? missingMessage
                : missingMessage);
    }

    private void UpdateResponsiveLayout(double width)
    {
        var useTwoColumns = width >= TwoColumnBreakpoint;
        if (useTwoColumns == _twoColumns && _sides.ColumnDefinitions.Count + _sides.RowDefinitions.Count > 0)
            return;

        _twoColumns = useTwoColumns;
        _sides.ColumnDefinitions.Clear();
        _sides.RowDefinitions.Clear();

        if (useTwoColumns)
        {
            _sides.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _sides.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_originalContainer, 0);
            Grid.SetColumn(_changedContainer, 1);
            Grid.SetRow(_originalContainer, 0);
            Grid.SetRow(_changedContainer, 0);
        }
        else
        {
            _sides.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _sides.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_originalContainer, 0);
            Grid.SetColumn(_changedContainer, 0);
            Grid.SetRow(_originalContainer, 0);
            Grid.SetRow(_changedContainer, 1);
        }
    }

    private static IEnumerable<string> BuildSummary(ImageDiffPreviewContent content)
    {
        if (content.Changes is not { } changes
            || content.Original.Metadata is not { } original
            || content.Changed.Metadata is not { } changed)
            yield break;

        if (changes.DimensionsChanged)
        {
            if (changes.DimensionsSwapped)
            {
                yield return $"Dimensions swapped: {original.Width} × {original.Height} → {changed.Width} × {changed.Height}";
                yield return "Possible 90° rotation";
            }
            else
            {
                yield return $"Resized: {original.Width} × {original.Height} → {changed.Width} × {changed.Height}";
            }
        }

        if (changes.FormatChanged)
        {
            yield return $"Format changed: {ImagePreviewFormatting.FormatImageFormat(original.Format)} → {ImagePreviewFormatting.FormatImageFormat(changed.Format)}";
        }

        if (changes.FileSizeChanged)
        {
            var absolute = $"File size: {ImagePreviewFormatting.FormatSize(content.Original.FileSize)} → {ImagePreviewFormatting.FormatSize(content.Changed.FileSize)}";
            if (content.Original.FileSize > 0)
            {
                var percent = (content.Changed.FileSize / (double)content.Original.FileSize - 1d) * 100d;
                var rounded = Math.Round(Math.Abs(percent), MidpointRounding.AwayFromZero);
                var sign = percent >= 0 ? "+" : "−";
                absolute += $" ({sign}{rounded:0}%)";
            }
            yield return absolute;
        }

        if (changes.RequiredMetadataUnchanged)
            yield return "Image content changed; dimensions, format and file size are unchanged.";
    }
}
