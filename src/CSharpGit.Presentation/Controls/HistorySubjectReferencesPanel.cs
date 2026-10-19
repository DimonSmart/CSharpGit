using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Controls;

/// <summary>
/// Shares the finite History message column between the subject and reference
/// badges. Children, in order: subject, reflog badge, reflog ghost, refs.
/// </summary>
public sealed class HistorySubjectReferencesPanel : Panel
{
    private readonly RectangleGeometry _clip = new();
    private readonly double[] _naturalWidths = new double[4];
    private readonly double[] _widths = new double[4];
    private double _lastWidth;

    public HistorySubjectReferencesPanel() => Clip = _clip;

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0d;
        var unconstrained = new Size(double.PositiveInfinity, availableSize.Height);
        for (var i = 0; i < Children.Count && i < 4; i++)
        {
            Children[i].Measure(unconstrained);
            _naturalWidths[i] = Children[i].DesiredSize.Width;
            height = Math.Max(height, Children[i].DesiredSize.Height);
        }

        var natural = _naturalWidths.Sum();
        _lastWidth = double.IsFinite(availableSize.Width)
            ? Math.Max(0, availableSize.Width)
            : natural;
        Allocate(_lastWidth);
        MeasureAllocated(availableSize.Height);

        for (var i = 0; i < Children.Count && i < 4; i++)
            height = Math.Max(height, Children[i].DesiredSize.Height);

        return new Size(Math.Min(natural, _lastWidth), Math.Min(height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _clip.Rect = new Rect(0, 0, Math.Max(0, finalSize.Width), Math.Max(0, finalSize.Height));
        if (Math.Abs(finalSize.Width - _lastWidth) > 0.01 && double.IsFinite(finalSize.Width))
        {
            Allocate(Math.Max(0, finalSize.Width));
            MeasureAllocated(finalSize.Height);
        }

        var x = 0d;
        for (var i = 0; i < Children.Count && i < 4; i++)
        {
            var child = Children[i];
            child.Arrange(new Rect(x, 0, _widths[i], finalSize.Height));
            x += _widths[i];
        }

        return finalSize;
    }

    private void Allocate(double width)
    {
        Array.Clear(_widths);
        if (Children.Count != 4)
            return;

        // Ghost and reflog labels must never consume the entire message column.
        // These caps apply only to the optional decorations, not to Git badges.
        _widths[1] = Math.Min(_naturalWidths[1], width * 0.3);
        _widths[2] = Math.Min(_naturalWidths[2], width * 0.35);
        var shared = Math.Max(0, width - _widths[1] - _widths[2]);

        if (_naturalWidths[0] + _naturalWidths[3] <= shared)
        {
            _widths[0] = _naturalWidths[0];
            _widths[3] = _naturalWidths[3];
            return;
        }

        if (_naturalWidths[3] <= 0)
        {
            _widths[0] = shared;
            return;
        }

        var subject = Children[0] as TextBlock;
        // Semantic readability floor, scaled by actual font size and available
        // width, not a fixed pixel width derived from a screenshot.
        var subjectFloor = Math.Min(_naturalWidths[0],
            Math.Min(shared * 0.45, (subject?.FontSize ?? 14) * 11));
        _widths[0] = Math.Min(_naturalWidths[0],
            Math.Max(subjectFloor, shared - _naturalWidths[3]));
        _widths[3] = Math.Max(0, shared - _widths[0]);
    }

    private void MeasureAllocated(double availableHeight)
    {
        for (var i = 0; i < Children.Count && i < 4; i++)
            Children[i].Measure(new Size(_widths[i], availableHeight));
    }
}
