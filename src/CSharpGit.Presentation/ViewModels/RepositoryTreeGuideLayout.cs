namespace CSharpGit.Presentation.ViewModels;

public enum RepositoryTreeGuideSegmentKind
{
    Empty,
    Continue,
    Branch,
    Last
}

public readonly record struct RepositoryTreeGuideLine(double X1, double Y1, double X2, double Y2);

public static class RepositoryTreeGuideLayout
{
    public const double FallbackSegmentWidth = 16d;

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

    public static IReadOnlyList<RepositoryTreeGuideLine> BuildLines(
        IReadOnlyList<RepositoryTreeGuideSegmentKind> segments,
        double totalIndentation,
        double rowHeight)
    {
        if (segments.Count == 0 || rowHeight <= 0)
            return Array.Empty<RepositoryTreeGuideLine>();

        var effectiveIndentation = totalIndentation > 0
            ? totalIndentation
            : segments.Count * FallbackSegmentWidth;
        var segmentWidth = effectiveIndentation / segments.Count;
        var left = -effectiveIndentation;
        var middleY = rowHeight / 2d;
        var lines = new List<RepositoryTreeGuideLine>(segments.Count * 2);

        for (var index = 0; index < segments.Count; index++)
        {
            var segmentLeft = left + (index * segmentWidth);
            var centerX = segmentLeft + (segmentWidth / 2d);
            var segmentRight = segmentLeft + segmentWidth;

            switch (segments[index])
            {
                case RepositoryTreeGuideSegmentKind.Continue:
                    lines.Add(new RepositoryTreeGuideLine(centerX, 0, centerX, rowHeight));
                    break;
                case RepositoryTreeGuideSegmentKind.Branch:
                    lines.Add(new RepositoryTreeGuideLine(centerX, 0, centerX, rowHeight));
                    lines.Add(new RepositoryTreeGuideLine(centerX, middleY, segmentRight, middleY));
                    break;
                case RepositoryTreeGuideSegmentKind.Last:
                    lines.Add(new RepositoryTreeGuideLine(centerX, 0, centerX, middleY));
                    lines.Add(new RepositoryTreeGuideLine(centerX, middleY, segmentRight, middleY));
                    break;
            }
        }

        return lines;
    }
}
