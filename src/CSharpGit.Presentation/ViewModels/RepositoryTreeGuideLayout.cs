namespace CSharpGit.Presentation.ViewModels;

public enum RepositoryTreeGuideSegmentKind
{
    Empty,
    Continue,
    Branch,
    Last
}

public readonly record struct RepositoryTreeGuideLine(double X1, double Y1, double X2, double Y2);

public readonly record struct RepositoryTreeGuideSegmentGeometry(
    RepositoryTreeGuideSegmentKind Kind,
    double CenterX,
    double RightX,
    double MiddleY,
    double CornerRadius);

public static class RepositoryTreeGuideLayout
{
    public const double FallbackSegmentWidth = 16d;
    public const double ExpanderBoxSize = 12d;
    public const double GuideCornerRadius = 3d;

    public static IReadOnlyList<RepositoryTreeGuideSegmentKind> BuildSegments(
        IReadOnlyList<bool> ancestorHasFollowingSiblings,
        bool isLastSibling)
    {
        var result = new RepositoryTreeGuideSegmentKind[ancestorHasFollowingSiblings.Count + 1];
        for (var index = 0; index < ancestorHasFollowingSiblings.Count; index++)
        {
            result[index] = ancestorHasFollowingSiblings[index]
                ? RepositoryTreeGuideSegmentKind.Continue
                : RepositoryTreeGuideSegmentKind.Empty;
        }

        result[^1] = isLastSibling
            ? RepositoryTreeGuideSegmentKind.Last
            : RepositoryTreeGuideSegmentKind.Branch;
        return result;
    }

    public static double ResolveIndentation(int segmentCount, double totalIndentation) =>
        segmentCount <= 0
            ? 0
            : totalIndentation > 0
                ? totalIndentation
                : segmentCount * FallbackSegmentWidth;

    public static double ResolveExpanderSurfaceWidth(int segmentCount, double totalIndentation) =>
        segmentCount > 0
            ? ResolveIndentation(segmentCount, totalIndentation)
            : FallbackSegmentWidth;

    public static double ResolveExpanderCenterX(int segmentCount, double totalIndentation)
    {
        var effectiveSegmentCount = Math.Max(1, segmentCount);
        var width = ResolveExpanderSurfaceWidth(segmentCount, totalIndentation);
        var segmentWidth = width / effectiveSegmentCount;
        return width - (segmentWidth / 2d);
    }

    public static IReadOnlyList<RepositoryTreeGuideSegmentGeometry> BuildGeometry(
        IReadOnlyList<RepositoryTreeGuideSegmentKind> segments,
        double totalIndentation,
        double rowHeight)
    {
        if (segments.Count == 0 || rowHeight <= 0)
            return Array.Empty<RepositoryTreeGuideSegmentGeometry>();

        var effectiveIndentation = ResolveIndentation(segments.Count, totalIndentation);
        var segmentWidth = effectiveIndentation / segments.Count;
        var middleY = rowHeight / 2d;
        var cornerRadius = Math.Min(GuideCornerRadius, Math.Min(segmentWidth / 4d, rowHeight / 4d));
        var result = new List<RepositoryTreeGuideSegmentGeometry>(segments.Count);

        for (var index = 0; index < segments.Count; index++)
        {
            if (segments[index] == RepositoryTreeGuideSegmentKind.Empty) continue;

            var segmentLeft = index * segmentWidth;
            var centerX = segmentLeft + (segmentWidth / 2d);
            result.Add(new RepositoryTreeGuideSegmentGeometry(
                segments[index],
                centerX,
                segmentLeft + segmentWidth,
                middleY,
                cornerRadius));
        }

        return result;
    }

    public static IReadOnlyList<RepositoryTreeGuideLine> BuildLines(
        IReadOnlyList<RepositoryTreeGuideSegmentKind> segments,
        double totalIndentation,
        double rowHeight)
    {
        var geometry = BuildGeometry(segments, totalIndentation, rowHeight);
        if (geometry.Count == 0)
            return Array.Empty<RepositoryTreeGuideLine>();

        var lines = new List<RepositoryTreeGuideLine>(geometry.Count * 2);
        foreach (var segment in geometry)
        {
            switch (segment.Kind)
            {
                case RepositoryTreeGuideSegmentKind.Continue:
                    lines.Add(new RepositoryTreeGuideLine(segment.CenterX, 0, segment.CenterX, rowHeight));
                    break;
                case RepositoryTreeGuideSegmentKind.Branch:
                    lines.Add(new RepositoryTreeGuideLine(segment.CenterX, 0, segment.CenterX, rowHeight));
                    lines.Add(new RepositoryTreeGuideLine(segment.CenterX, segment.MiddleY, segment.RightX, segment.MiddleY));
                    break;
                case RepositoryTreeGuideSegmentKind.Last:
                    lines.Add(new RepositoryTreeGuideLine(segment.CenterX, 0, segment.CenterX, segment.MiddleY));
                    lines.Add(new RepositoryTreeGuideLine(segment.CenterX, segment.MiddleY, segment.RightX, segment.MiddleY));
                    break;
            }
        }

        return lines;
    }
}
