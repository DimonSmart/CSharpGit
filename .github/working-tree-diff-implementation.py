from pathlib import Path

ROOT = Path.cwd()


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content, encoding="utf-8")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one occurrence, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


def append_section(path: str, marker: str, section: str) -> None:
    text = read(path)
    if marker in text:
        return
    write(path, text.rstrip() + "\n\n" + section.strip() + "\n")


# Git service: make the existing service extensible by a focused partial and fix Discard safety.
replace_once(
    "src/CSharpGit.Git/GitCliRepositoryService.cs",
    "public sealed class GitCliRepositoryService : IRepositoryService, IRepositoryStateService, IHistoryService, IWorkingTreeService, IReferenceService, IRepositoryWorkflowService",
    "public sealed partial class GitCliRepositoryService : IRepositoryService, IRepositoryStateService, IHistoryService, IWorkingTreeService, IReferenceService, IRepositoryWorkflowService")

git_path = "src/CSharpGit.Git/GitCliRepositoryService.cs"
git = read(git_path)
start = git.index("    public async Task DiscardFileAsync(")
end = git.index("    public async Task CommitAsync(", start)
new_discard = '''    public async Task DiscardFileAsync(Repository repository, WorkingTreeChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(change);
        ValidateChange(change);
        if (!change.IsUnstaged)
            throw new InvalidOperationException("Only unstaged working-tree changes can be discarded.");

        cancellationToken.ThrowIfCancellationRequested();
        if (change.IndexStatus == '?')
        {
            var fullPath = ResolveSafeWorkingTreePath(repository, change.Path);
            if (File.Exists(fullPath)) File.Delete(fullPath);
            return;
        }

        // For an unstaged rename, the index still contains OriginalPath. Remove the
        // renamed working-tree file and restore the indexed path without touching the index.
        if (change.WorkingTreeStatus == 'R' && change.OriginalPath is not null)
        {
            var renamedPath = ResolveSafeWorkingTreePath(repository, change.Path);
            if (File.Exists(renamedPath)) File.Delete(renamedPath);
            await RunGitForMutationAsync(repository, cancellationToken, "restore", "--worktree", "--", change.OriginalPath);
            return;
        }

        // Default restore source is the index. Do not use --staged or --source=HEAD:
        // staged content must survive discarding the additional working-tree delta.
        await RunGitForMutationAsync(repository, cancellationToken, "restore", "--worktree", "--", change.Path);
    }

'''
write(git_path, git[:start] + new_discard + git[end:])

# DI registration for the read-only diff service.
replace_once(
    "src/CSharpGit.Presentation/App.xaml.cs",
    "                services.AddSingleton<IWorkingTreeService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());\n",
    "                services.AddSingleton<IWorkingTreeService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());\n"
    "                services.AddSingleton<IWorkingTreeDiffService>(provider => (GitCliRepositoryService)provider.GetRequiredService<IRepositoryService>());\n")

# Explicit side selection in the ViewModel prevents actions for the other side of a dual-status file.
vm_path = "src/CSharpGit.Presentation/ViewModels/OpenRepositoryViewModel.cs"
replace_once(
    vm_path,
    "    private WorkingTreeChange? _selectedChange;\n",
    "    private WorkingTreeChange? _selectedChange;\n"
    "    private WorkingTreeDiffKind? _selectedWorkingTreeDiffKind;\n"
    "    private FileDiff? _selectedWorkingTreeDiff;\n")
replace_once(
    vm_path,
    "        StageCommand = new AsyncCommand(() => MutateAsync(() => _workingTreeService.StageFileAsync(Repository!, SelectedChange!)), () => CanMutate() && SelectedChange is { IsUnstaged: true });",
    "        StageCommand = new AsyncCommand(() => MutateAsync(() => _workingTreeService.StageFileAsync(Repository!, SelectedChange!)), () => CanMutate() && SelectedWorkingTreeDiffKind == WorkingTreeDiffKind.Unstaged && SelectedChange is { IsUnstaged: true });")
replace_once(
    vm_path,
    "        UnstageCommand = new AsyncCommand(() => MutateAsync(() => _workingTreeService.UnstageFileAsync(Repository!, SelectedChange!)), () => CanMutate() && SelectedChange is { IsStaged: true });",
    "        UnstageCommand = new AsyncCommand(() => MutateAsync(() => _workingTreeService.UnstageFileAsync(Repository!, SelectedChange!)), () => CanMutate() && SelectedWorkingTreeDiffKind == WorkingTreeDiffKind.Staged && SelectedChange is { IsStaged: true });")
replace_once(
    vm_path,
    "        RequestDiscardCommand = new AsyncCommand(RequestDiscardAsync, () => CanMutate() && SelectedChange is not null);",
    "        RequestDiscardCommand = new AsyncCommand(RequestDiscardAsync, () => CanMutate() && SelectedWorkingTreeDiffKind == WorkingTreeDiffKind.Unstaged && SelectedChange is { IsUnstaged: true });")
selected_change = "    public WorkingTreeChange? SelectedChange { get => _selectedChange; set { _selectedChange = value; Notify(); RaiseCommands(); } }\n"
replace_once(
    vm_path,
    selected_change,
    selected_change + '''    public WorkingTreeDiffKind? SelectedWorkingTreeDiffKind
    {
        get => _selectedWorkingTreeDiffKind;
        set
        {
            if (_selectedWorkingTreeDiffKind == value) return;
            _selectedWorkingTreeDiffKind = value;
            Notify();
            RaiseCommands();
        }
    }
    public FileDiff? SelectedWorkingTreeDiff
    {
        get => _selectedWorkingTreeDiff;
        set
        {
            if (ReferenceEquals(_selectedWorkingTreeDiff, value)) return;
            _selectedWorkingTreeDiff = value;
            Notify();
        }
    }
''')

# MainPage receives the service while retaining direct-test source compatibility.
page_path = "src/CSharpGit.Presentation/MainPage.xaml.cs"
replace_once(
    page_path,
    "    public MainPage(OpenRepositoryViewModel viewModel, IReferenceHistoryService referenceHistoryService, IReferenceService referenceService)\n",
    "    public MainPage(OpenRepositoryViewModel viewModel, IReferenceHistoryService referenceHistoryService, IReferenceService referenceService, IWorkingTreeDiffService? workingTreeDiffService = null)\n")
replace_once(
    page_path,
    "        _referenceService = referenceService;\n",
    "        _referenceService = referenceService;\n        _workingTreeDiffService = workingTreeDiffService;\n")
replace_once(
    page_path,
    "        Loaded += RunDesktopCheckWhenRequested;\n        RefreshPresentationCollections();\n    }\n",
    "        Loaded += RunDesktopCheckWhenRequested;\n        RefreshPresentationCollections();\n        InitializeWorkingTreeDiffSurface();\n    }\n")

# Replace only the Working Tree center; the bottom commit area stays intact.
xaml_path = "src/CSharpGit.Presentation/MainPage.xaml"
xaml = read(xaml_path)
pane_start = xaml.index('          <Grid x:Name="WorkingTreePane"')
center_start = xaml.index('            <Grid Grid.Row="1" RowDefinitions="*,12,*">', pane_start)
center_end = xaml.index('            <Border Grid.Row="2"', center_start)
center = '''            <Grid Grid.Row="1" ColumnDefinitions="330,6,*" MinHeight="180">
              <Grid Grid.Column="0" RowDefinitions="*,12,*">
                <Grid Grid.Row="0" RowDefinitions="Auto,*,Auto">
                  <TextBlock x:Name="UnstagedHeader" Text="Unstaged changes" FontWeight="SemiBold" Margin="0,0,0,6" />
                  <ListView x:Name="UnstagedChangesList" Grid.Row="1" SelectionChanged="UnstagedChangesList_SelectionChanged">
                    <ListView.ItemTemplate>
                      <DataTemplate><Grid ColumnDefinitions="32,*" Padding="6,3"><TextBlock Text="{Binding WorkingTreeStatus}" FontFamily="Consolas" /><TextBlock Grid.Column="1" Text="{Binding Path}" /></Grid></DataTemplate>
                    </ListView.ItemTemplate>
                  </ListView>
                  <StackPanel Grid.Row="2" Orientation="Horizontal" Spacing="8" Margin="0,6,0,0">
                    <Button Content="Stage" Command="{Binding StageCommand}" />
                    <Button Content="Discard…" Command="{Binding RequestDiscardCommand}" />
                  </StackPanel>
                </Grid>

                <Grid Grid.Row="2" RowDefinitions="Auto,*,Auto">
                  <TextBlock x:Name="StagedHeader" Text="Staged changes" FontWeight="SemiBold" Margin="0,0,0,6" />
                  <ListView x:Name="StagedChangesList" Grid.Row="1" SelectionChanged="StagedChangesList_SelectionChanged">
                    <ListView.ItemTemplate>
                      <DataTemplate><Grid ColumnDefinitions="32,*" Padding="6,3"><TextBlock Text="{Binding IndexStatus}" FontFamily="Consolas" /><TextBlock Grid.Column="1" Text="{Binding Path}" /></Grid></DataTemplate>
                    </ListView.ItemTemplate>
                  </ListView>
                  <Button Grid.Row="2" Content="Unstage" Command="{Binding UnstageCommand}" HorizontalAlignment="Left" Margin="0,6,0,0" />
                </Grid>
              </Grid>

              <controls:GridSplitter Grid.Column="1" ResizeDirection="Columns" ResizeBehavior="PreviousAndNext"
                                     MinimumFirst="220" MinimumSecond="280"
                                     HorizontalAlignment="Stretch" Background="{ThemeResource DividerStrokeColorDefaultBrush}" />

              <Grid Grid.Column="2" RowDefinitions="36,22,*" Margin="10,0,0,0">
                <Border BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}" BorderThickness="0,0,0,1" Padding="8,0">
                  <Grid ColumnDefinitions="*,Auto" ColumnSpacing="8">
                    <TextBlock x:Name="WorkingTreeDiffHeader" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" VerticalAlignment="Center" />
                    <TextBlock x:Name="WorkingTreeDiffKindText" Grid.Column="1" FontSize="11" FontWeight="SemiBold" Opacity="0.72" VerticalAlignment="Center" />
                  </Grid>
                </Border>

                <Border Grid.Row="1" BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}" BorderThickness="0,0,0,1">
                  <Grid ColumnDefinitions="38,38,*">
                    <TextBlock Text="OLD" FontSize="9" Opacity="0.52" HorizontalAlignment="Right" Margin="0,0,5,0" VerticalAlignment="Center" />
                    <TextBlock Grid.Column="1" Text="NEW" FontSize="9" Opacity="0.52" HorizontalAlignment="Right" Margin="0,0,5,0" VerticalAlignment="Center" />
                  </Grid>
                </Border>

                <Grid Grid.Row="2">
                  <InfoBar x:Name="WorkingTreeBinaryInfo" IsOpen="True" Severity="Informational" Title="Binary file"
                           Message="Binary file — text diff is not available." Visibility="Collapsed" IsClosable="False" VerticalAlignment="Top" />
                  <InfoBar x:Name="WorkingTreeNoChangesInfo" IsOpen="True" Severity="Informational" Title="No changes"
                           Message="The selected delta no longer exists." Visibility="Collapsed" IsClosable="False" VerticalAlignment="Top" />
                  <ListView x:Name="WorkingTreeCompactDiffList" Visibility="Collapsed" SelectionMode="None"
                            HorizontalContentAlignment="Stretch"
                            ScrollViewer.HorizontalScrollMode="Auto"
                            ScrollViewer.HorizontalScrollBarVisibility="Auto"
                            ScrollViewer.VerticalScrollMode="Auto"
                            ScrollViewer.VerticalScrollBarVisibility="Auto" />
                </Grid>
              </Grid>
            </Grid>

'''
write(xaml_path, xaml[:center_start] + center + xaml[center_end:])
replace_once(
    xaml_path,
    'Title="Confirm discarding all changes to the file" Message="This action irreversibly replaces the selected file with its HEAD version (or deletes an untracked file)."',
    'Title="Confirm discarding unstaged changes" Message="This action restores the working-tree version from the index (or deletes an untracked file). Staged changes are preserved."')

# Durable intent stays in the existing product intents.
append_section(
    ".idd/intent/IDD-0005.spec-working-tree-and-commits.md",
    "## Selected-file Working Tree diff",
    '''## Selected-file Working Tree diff

- Working Tree keeps staged and unstaged deltas as two distinct user-visible states.
- Selecting an unstaged row reads and shows only `index -> working tree`; selecting a staged row reads and shows only `HEAD -> index`.
- A file that has both staged and additional unstaged edits may appear in both lists and produces two different diffs.
- Diff loading is lazy (selection only), cancellable, and guarded against stale completion after selection or repository state changes.
- Added, deleted, renamed, untracked, binary, Unicode/space paths, and unborn-HEAD staged changes retain Git semantics. Unmerged entries defer to the conflict workflow rather than presenting an incorrect two-way diff.
- `Discard...` in the Unstaged section restores the working tree from the index and must preserve staged content; untracked deletion remains explicit and confirmed.
''')
append_section(
    ".idd/intent/IDD-0004.spec-history-and-diff.md",
    "## Reusable compact diff presentation",
    '''## Reusable compact diff presentation

The compact unified-diff presentation (OLD/NEW line numbers, hunks, context/add/remove highlighting, binary state, and standard Git-header suppression) is reusable product UI. Commit Changes and the selected-file Working Tree diff use the same `FileDiff -> DiffLine -> CompactDiffLine` semantics and the same compact item template/resources.
''')
append_section(
    ".idd/intent/IDD-0010.spec-history-first-main-workspace.md",
    "## Working Tree detail surface",
    '''## Working Tree detail surface

Working Tree mode keeps the commit area at the bottom and presents a resizable workspace above it: staged/unstaged lists and their actions on the left, with the currently selected staged-or-unstaged compact diff on the right. Only one side of a file is visually selected at a time.
''')

print("Working Tree diff patches applied.")
