using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class RepositoryTreeGuideLayoutTests
{
    [Fact]
    public void BuildSegments_marks_intermediate_and_last_children()
    {
        Assert.Equal(
            new[] { RepositoryTreeGuideSegmentKind.Branch },
            RepositoryTreeGuideLayout.BuildSegments(Array.Empty<bool>(), isLastSibling: false));
        Assert.Equal(
            new[] { RepositoryTreeGuideSegmentKind.Last },
            RepositoryTreeGuideLayout.BuildSegments(Array.Empty<bool>(), isLastSibling: true));
    }

    [Fact]
    public void BuildSegments_preserves_ancestor_continuations_at_arbitrary_depth()
    {
        var segments = RepositoryTreeGuideLayout.BuildSegments(
            new[] { true, false, true },
            isLastSibling: true);

        Assert.Equal(
            new[]
            {
                RepositoryTreeGuideSegmentKind.Continue,
                RepositoryTreeGuideSegmentKind.Empty,
                RepositoryTreeGuideSegmentKind.Continue,
                RepositoryTreeGuideSegmentKind.Last
            },
            segments);
    }

    [Fact]
    public void BuildLines_draws_continuation_and_terminating_last_connector()
    {
        var lines = RepositoryTreeGuideLayout.BuildLines(
            new[]
            {
                RepositoryTreeGuideSegmentKind.Continue,
                RepositoryTreeGuideSegmentKind.Empty,
                RepositoryTreeGuideSegmentKind.Last
            },
            totalIndentation: 48,
            rowHeight: 24);

        Assert.Equal(
            new[]
            {
                new RepositoryTreeGuideLine(8, 0, 8, 24),
                new RepositoryTreeGuideLine(40, 0, 40, 12),
                new RepositoryTreeGuideLine(40, 12, 48, 12)
            },
            lines);
    }

    [Fact]
    public void BuildGeometry_provides_a_small_rounded_elbow_for_branch_connectors()
    {
        var geometry = RepositoryTreeGuideLayout.BuildGeometry(
            new[]
            {
                RepositoryTreeGuideSegmentKind.Continue,
                RepositoryTreeGuideSegmentKind.Empty,
                RepositoryTreeGuideSegmentKind.Last
            },
            totalIndentation: 48,
            rowHeight: 24);

        Assert.Equal(
            new[]
            {
                new RepositoryTreeGuideSegmentGeometry(RepositoryTreeGuideSegmentKind.Continue, 8, 16, 12, 3),
                new RepositoryTreeGuideSegmentGeometry(RepositoryTreeGuideSegmentKind.Last, 40, 48, 12, 3)
            },
            geometry);
    }

    [Fact]
    public void BuildLines_uses_fallback_indentation_when_template_indentation_is_unavailable()
    {
        var lines = RepositoryTreeGuideLayout.BuildLines(
            new[] { RepositoryTreeGuideSegmentKind.Last },
            totalIndentation: 0,
            rowHeight: 24);

        Assert.Equal(
            new[]
            {
                new RepositoryTreeGuideLine(8, 0, 8, 12),
                new RepositoryTreeGuideLine(8, 12, 16, 12)
            },
            lines);
    }

    [Fact]
    public void Expander_is_centered_in_current_indent_column()
    {
        Assert.Equal(40, RepositoryTreeGuideLayout.ResolveExpanderCenterX(segmentCount: 3, totalIndentation: 48));
        Assert.Equal(48, RepositoryTreeGuideLayout.ResolveExpanderSurfaceWidth(segmentCount: 3, totalIndentation: 48));
    }

    [Fact]
    public void Root_expander_uses_one_fallback_indent_column_without_root_connector()
    {
        Assert.Equal(8, RepositoryTreeGuideLayout.ResolveExpanderCenterX(segmentCount: 0, totalIndentation: 0));
        Assert.Equal(16, RepositoryTreeGuideLayout.ResolveExpanderSurfaceWidth(segmentCount: 0, totalIndentation: 0));
    }
}
