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
        Assert.Contains(@"AuthorName=""{x:Bind Commit.Author, Mode=OneWay}""", history, StringComparison.Ordinal);
        Assert.Contains(@"AuthorEmail=""{x:Bind Commit.AuthorEmail, Mode=OneWay}""", history, StringComparison.Ordinal);
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
        Assert.Contains("OnlineAvatarLookupEnabled", control, StringComparison.Ordinal);
        Assert.Contains("_requestGate.IsCurrent", control, StringComparison.Ordinal);
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
        Assert.Contains("string.Equals(_fallbackIdentity, identity", control, StringComparison.Ordinal);
        Assert.DoesNotContain("((AuthorAvatar)dependencyObject).Refresh();", control, StringComparison.Ordinal);
    }

    [Fact]
    public void AvatarLifecycleDoesNotTurnVisualEventsIntoResolveLifecycle()
    {
        var root = FindRepositoryRoot();
        var control = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "AuthorAvatar.xaml.cs"));
        var gate = File.ReadAllText(Path.Combine(root, "src", "CSharpGit.Presentation", "Controls", "AuthorAvatarRequestGate.cs"));

        var unloaded = MethodBlock(control, "private void AuthorAvatar_Unloaded");
        var avatarSizeChanged = MethodBlock(control, "private static void AvatarSizePropertyChanged");

        Assert.DoesNotContain("_requestGate.Cancel", unloaded, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleRefresh", avatarSizeChanged, StringComparison.Ordinal);
        Assert.Contains("AuthorAvatarLifecycleState", control, StringComparison.Ordinal);
        Assert.Contains("ResolvedNoImage", control, StringComparison.Ordinal);
        Assert.Contains("AvatarResolveFaulted", control, StringComparison.Ordinal);
        Assert.Contains("_requestGate.TryComplete", control, StringComparison.Ordinal);
        Assert.Contains("request.CancellationSource.Dispose()", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshNow();\n        ResolveAsync", control, StringComparison.Ordinal);
    }

    private static string MethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0);
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[start..(index + 1)];
        }

        throw new InvalidOperationException($"Method body is incomplete: {signature}");
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
