namespace CSharpGit.Desktop.Tests;

public sealed class AuthorAvatarUiContractTests
{
    [Fact]
    public void HistoryUsesInlineAvatarWithoutChangingHistoryMetadataFlow()
    {
        var root = FindRepositoryRoot();
        var history = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Styles", "HistoryReferences.xaml"));
        var mainPage = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.xaml"));
        var lifecycle = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.CommitGraphLayout.cs"));

        Assert.Contains("<controls:AuthorAvatar", history, StringComparison.Ordinal);
        Assert.Contains(@"AuthorName=""{Binding Commit.Author}""", history, StringComparison.Ordinal);
        Assert.Contains(@"AuthorEmail=""{Binding Commit.AuthorEmail}""", history, StringComparison.Ordinal);
        Assert.Contains(@"AvatarSize=""18""", history, StringComparison.Ordinal);
        Assert.Contains(@"<ColumnDefinition Width=""160"" />", mainPage, StringComparison.Ordinal);
        Assert.Contains(@"MinHeight=""{StaticResource Height.DataRow}""", history, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigureAuthorAvatars", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("await avatar", history, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommitDetailsUsesSelectedHistoryMetadataAndSharedAvatarService()
    {
        var root = FindRepositoryRoot();
        var details = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitDetailsView.xaml"));
        var detailsCode = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "CommitDetailsView.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryMaintenance.cs"));
        var presentationFiles = Directory.EnumerateFiles(
                Path.Combine(root, "src", "CSharpGit.Presentation"),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Contains(@"AuthorName=""{Binding SelectedHistoryRow.Commit.Author}""", details, StringComparison.Ordinal);
        Assert.Contains(@"AuthorEmail=""{Binding SelectedHistoryRow.Commit.AuthorEmail}""", details, StringComparison.Ordinal);
        Assert.Contains(@"AvatarSize=""32""", details, StringComparison.Ordinal);
        Assert.Contains("ConfigureAuthorAvatar", detailsCode, StringComparison.Ordinal);
        Assert.Contains("_authorAvatarService", composition, StringComparison.Ordinal);
        Assert.DoesNotContain(presentationFiles, source => source.Contains("ReadCommitAsync(", StringComparison.Ordinal));
    }

    [Fact]
    public void AvatarSettingsCollapseSpaceAndPreventLateOnlineResult()
    {
        var root = FindRepositoryRoot();
        var control = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "AuthorAvatar.xaml.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "SettingsPage.xaml"));

        Assert.Contains("Visibility = Visibility.Collapsed", control, StringComparison.Ordinal);
        Assert.Contains("OnlineAvatarLookupEnabled: true", control, StringComparison.Ordinal);
        Assert.Contains("_requestGate.IsCurrent(request, CurrentIdentity())", control, StringComparison.Ordinal);
        Assert.Contains("Show author avatars", settings, StringComparison.Ordinal);
        Assert.Contains("Load avatar images from online services", settings, StringComparison.Ordinal);
        Assert.Contains("a SHA-256 hash derived from the commit author email", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void AvatarControlUsesPresentationContextWithoutHistoryTreeTraversal()
    {
        var root = FindRepositoryRoot();
        var control = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "AuthorAvatar.xaml.cs"));
        var host = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.AuthorAvatars.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "MainPage.RepositoryMaintenance.cs"));

        Assert.Contains("internal void Configure(", control, StringComparison.Ordinal);
        Assert.Contains("AuthorAvatarServiceContext.TryGet", control, StringComparison.Ordinal);
        Assert.Contains("AuthorAvatarServiceContext.Configure", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigureAuthorAvatars", host, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper", host, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService", control, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceProvider", control, StringComparison.Ordinal);
    }

    [Fact]
    public void AvatarIdentityBindingChangesAreCoalescedBeforeRefresh()
    {
        var root = FindRepositoryRoot();
        var control = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "AuthorAvatar.xaml.cs"));

        Assert.Contains("ScheduleRefresh()", control, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _refreshScheduled, 1)", control, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue", control, StringComparison.Ordinal);
        Assert.Contains("string.Equals(_displayedRemoteIdentity, identity", control, StringComparison.Ordinal);
        Assert.Contains("string.Equals(_fallbackIdentity, identity", control, StringComparison.Ordinal);
        Assert.DoesNotContain("((AuthorAvatar)dependencyObject).Refresh();", control, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpGit.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CSharpGit repository root.");
    }
}
