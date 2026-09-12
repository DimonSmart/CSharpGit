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
                new RepositoryTreeGuideLine(-40, 0, -40, 24),
                new RepositoryTreeGuideLine(-8, 0, -8, 12),
                new RepositoryTreeGuideLine(-8, 12, 0, 12)
            },
            lines);
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
                new RepositoryTreeGuideLine(-8, 0, -8, 12),
                new RepositoryTreeGuideLine(-8, 12, 0, 12)
            },
            lines);
    }
}
