namespace CSharpGit.Application.Tests;

public sealed class CommitDraftCloseConfirmationOwnershipContractTests
{
    [Fact]
    public void WindowCompositionOwnsCloseDialogAndRejectsStaleRepositoryResults()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "CSharpGit.Presentation");
        var page = File.ReadAllText(Path.Combine(presentation, "Composition/WindowWorkspaceComposition.cs"));
        var app = File.ReadAllText(Path.Combine(presentation, "App.xaml.cs"));
        var factory = File.ReadAllText(Path.Combine(presentation, "Windowing", "WindowSessionFactory.cs"));
        var dialog = File.ReadAllText(Path.Combine(presentation, "Windowing", "CommitDraftCloseConfirmationDialog.cs"));

        Assert.Contains("class CommitDraftCloseConfirmationDialog(", dialog);
        Assert.Contains("repository.CommitCreation.HasUnappliedCommitMessage", dialog);
        Assert.Contains("ReferenceEquals(originalRepository, repository.Repository)", dialog);
        Assert.Contains("!cancellationToken.IsCancellationRequested", dialog);
        Assert.Contains("!isShuttingDown()", dialog);
        Assert.Contains("composition.ShowCommitDraftDialogAsync", factory);
        Assert.Contains("composition.ConfigureCloseConfirmation(confirmation);", factory);
        Assert.Contains("windowContent.Confirmation", app);
        Assert.Contains("ShowPageDialogAsync(dialog)", page);
        Assert.DoesNotContain("class MainPage : Page, Windowing.ICloseConfirmationDialog", page);
        Assert.DoesNotContain("Title = \"Discard commit message?\"", page);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("CSharpGit repository root was not found.");
    }
}
