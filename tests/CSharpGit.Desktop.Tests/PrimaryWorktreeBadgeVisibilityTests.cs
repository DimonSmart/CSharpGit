using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class PrimaryWorktreeBadgeVisibilityTests
{
    [Fact]
    public void TwoWorktreesRemainTwoRowsWhenOnlyOneIsPrimary()
    {
        var primary = Worktree("E:/Work/ADG-PSN/OneTalent", "feature/rec-050-006-add-feedback-e2e", primary: true);
        var linked = Worktree("E:/Work/ADG-PSN/OneTalent-fix-offer-seed", "fix/rec-offer-status-seed", current: true);

        var root = RepositoryTreeDescriptorBuilder.BuildWorktreesRoot([linked, primary]);

        Assert.Equal(2, root.Children.Count);
        Assert.Equal("feature/rec-050-006-add-feedback-e2e", WorktreePresentation.GetPrimaryLabel((WorktreeInfo)root.Children[0].Value!));
        Assert.Equal("fix/rec-offer-status-seed", WorktreePresentation.GetPrimaryLabel((WorktreeInfo)root.Children[1].Value!));
    }

    private static WorktreeInfo Worktree(
        string path,
        string branch,
        bool primary = false,
        bool current = false) =>
        new(path, "1234567890", branch, current, false, false, null, false)
        {
            IsPrimary = primary
        };
}
