namespace CSharpGit.Application.Tests;

public sealed class ConfirmationStateCleanupContractTests
{
    [Fact]
    public void LegacyInlineConfirmationStateIsRemoved()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.cs"));
        var discardViewModel = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "ViewModels", "OpenRepositoryViewModel.Discard.cs"));

        Assert.DoesNotContain("EmptyIndexChoiceVisibility", xaml);
        Assert.DoesNotContain("EmptyIndexChoiceVisibility", viewModel);
        Assert.DoesNotContain("BatchDiscardConfirmationVisibility", xaml);
        Assert.DoesNotContain("BatchDiscardConfirmationVisibility", discardViewModel);
        Assert.DoesNotContain("DiscardConfirmationVisibility", viewModel);
        Assert.DoesNotContain("DiscardConfirmationMessage", viewModel);
        Assert.DoesNotContain("PendingDiscard", viewModel);
        Assert.DoesNotContain("RequestDiscardCommand", viewModel);
        Assert.DoesNotContain("ConfirmDiscardCommand", viewModel);
        Assert.DoesNotContain("CancelDiscardCommand", viewModel);
    }

    [Fact]
    public void DetachedTagCheckoutUsesSafeModalConfirmation()
    {
        var root = FindRepositoryRoot();
        var branchActions = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.BranchDeletion.cs"));

        Assert.Contains("Title = \"Checkout detached HEAD?\"", branchActions);
        Assert.Contains("PrimaryButtonText = \"Checkout detached\"", branchActions);
        Assert.Contains("CloseButtonText = \"Cancel\"", branchActions);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", branchActions);
        Assert.Contains("ConfirmCheckoutTagDetachedAsync(tag)", branchActions);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
