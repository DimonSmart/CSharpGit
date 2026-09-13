from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_method(path: Path, signature: str, replacement: str) -> None:
    text = path.read_text(encoding="utf-8")
    start = text.find(signature)
    if start < 0:
        raise RuntimeError(f"Signature not found in {path}: {signature}")
    brace = text.find("{", start)
    if brace < 0:
        raise RuntimeError(f"Opening brace not found in {path}: {signature}")
    depth = 0
    end = None
    for i in range(brace, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                end = i + 1
                break
    if end is None:
        raise RuntimeError(f"Method not balanced in {path}: {signature}")
    path.write_text(text[:start] + replacement.rstrip() + text[end:], encoding="utf-8")


def replace_exact(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one match in {path}, got {count}: {old[:100]!r}")
    path.write_text(text.replace(old, new), encoding="utf-8")


def replace_section(path: Path, heading: str, next_heading: str, body: str) -> None:
    text = path.read_text(encoding="utf-8")
    start = text.find(heading)
    if start < 0:
        raise RuntimeError(f"Heading not found in {path}: {heading}")
    end = text.find(next_heading, start + len(heading))
    if end < 0:
        raise RuntimeError(f"Next heading not found in {path}: {next_heading}")
    replacement = heading + "\n\n" + body.strip() + "\n\n"
    path.write_text(text[:start] + replacement + text[end:], encoding="utf-8")


main = ROOT / "src/CSharpGit.Presentation/MainPage.xaml.cs"
replace_exact(
    main,
    '''        if (eventArgs.PropertyName is nameof(OpenRepositoryViewModel.Repository) or nameof(OpenRepositoryViewModel.HeadDisplay) or nameof(OpenRepositoryViewModel.CurrentOperation))\n        {\n            RebuildRepositoryTree();\n            UpdateStatusBar();\n        }''',
    '''        if (eventArgs.PropertyName is nameof(OpenRepositoryViewModel.Repository) or nameof(OpenRepositoryViewModel.HeadDisplay) or nameof(OpenRepositoryViewModel.CurrentOperation))\n        {\n            UpdateStatusBar();\n        }''')
replace_exact(
    main,
    '''        RebuildRepositoryTree();\n        UpdateStatusBar();\n    }\n\n    private void RebuildRepositoryTree()''',
    '''        UpdateStatusBar();\n    }\n\n    private void RebuildRepositoryTree()''')
replace_method(
    main,
    "    private void RebuildRepositoryTree()",
    '''    private void RebuildRepositoryTree()\n    {\n        SynchronizeRepositoryTree();\n    }''')


tags = ROOT / "src/CSharpGit.Presentation/MainPage.Tags.cs"
replace_method(
    tags,
    "    private void ApplyTagOrderingToRepositoryTree()",
    '''    private void ApplyTagOrderingToRepositoryTree()\n    {\n        SynchronizeRepositoryTree();\n    }''')


worktrees = ROOT / "src/CSharpGit.Presentation/MainPage.Worktrees.cs"
replace_method(
    worktrees,
    "    internal void InitializeWorktreeSupport(IWorktreeService worktreeService)",
    '''    internal void InitializeWorktreeSupport(IWorktreeService worktreeService)\n    {\n        _worktreeService = worktreeService ?? throw new ArgumentNullException(nameof(worktreeService));\n        _viewModel.PropertyChanged += WorktreeViewModel_PropertyChanged;\n        QueueWorktreeRefresh();\n    }''')
replace_method(
    worktrees,
    "    private void WorktreeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)",
    '''    private void WorktreeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)\n    {\n        if (args.PropertyName is nameof(OpenRepositoryViewModel.Repository)\n            or nameof(OpenRepositoryViewModel.HeadDisplay))\n            QueueWorktreeRefresh();\n    }''')
replace_method(
    worktrees,
    "    private async Task RefreshWorktreePresentationAsync()",
    '''    private async Task RefreshWorktreePresentationAsync()\n    {\n        if (_worktreeService is null) return;\n        var repository = _viewModel.Repository;\n        if (repository is null)\n        {\n            _worktrees = [];\n            return;\n        }\n\n        try\n        {\n            var worktrees = await _worktreeService.ListAsync(repository);\n            if (!ReferenceEquals(repository, _viewModel.Repository)) return;\n            _worktrees = worktrees;\n            SynchronizeWorktreePresentation();\n        }\n        catch (OperationCanceledException)\n        {\n        }\n        catch (Exception exception)\n        {\n            Debug.WriteLine($"Could not refresh worktrees: {exception}");\n        }\n    }''')
for signature in [
    "    private void InstallWorktreeRoot()",
    "    private void RemoveWorktreeRoot()",
    "    private void ApplyBranchWorktreeIndicators()",
    "    private static void ApplyBranchWorktreeIndicators("
]:
    replace_method(worktrees, signature, "")


project = ROOT / "tests/CSharpGit.Desktop.Tests/CSharpGit.Desktop.Tests.csproj"
replace_exact(
    project,
    '''    <Compile Include="../../src/CSharpGit.Presentation/ViewModels/RepositoryTreeGuideLayout.cs" Link="RepositoryTree/RepositoryTreeGuideLayout.cs" />''',
    '''    <Compile Include="../../src/CSharpGit.Presentation/ViewModels/RepositoryTreeGuideLayout.cs" Link="RepositoryTree/RepositoryTreeGuideLayout.cs" />\n    <Compile Include="../../src/CSharpGit.Presentation/ViewModels/RepositoryTreeNodeKind.cs" Link="RepositoryTree/RepositoryTreeNodeKind.cs" />\n    <Compile Include="../../src/CSharpGit.Presentation/ViewModels/RepositoryTreeDescriptor.cs" Link="RepositoryTree/RepositoryTreeDescriptor.cs" />\n    <Compile Include="../../src/CSharpGit.Presentation/ViewModels/IncrementalTreeReconciler.cs" Link="RepositoryTree/IncrementalTreeReconciler.cs" />\n    <Compile Include="../../src/CSharpGit.Presentation/ViewModels/RepositoryTreeExpansion.cs" Link="RepositoryTree/RepositoryTreeExpansion.cs" />\n    <Compile Include="../../src/CSharpGit.Presentation/ViewModels/RepositoryTreeSelection.cs" Link="RepositoryTree/RepositoryTreeSelection.cs" />''')


idd12 = ROOT / ".idd/intent/IDD-0012.spec-repository-tree-navigation.md"
replace_exact(
    idd12,
    "Metadata пересчитывается при presentation rebuild, поэтому branch grouping по `/`, repository refresh, current branch change и local/remote trees остаются согласованными.",
    "Derived hierarchy-guide metadata может пересчитываться проходом по stable presentation tree после reconciliation; `PropertyChanged` отправляется только узлам, чья фактическая guide geometry изменилась.")
replace_section(
    idd12,
    "## Expansion state during the session",
    "## Architecture",
    '''Ручное expand/collapse является пользовательским presentation state и сохраняется при обычных repository refresh/mutations благодаря сохранению identity существующих `RepositoryTreeNode`.

Обычный refresh не должен collapse/expand существующие groups, remotes или branch folders и не должен заменять их presentation objects. Stable expansion storage остаётся fallback для nodes, которые действительно создаются заново.

При переключении current local branch путь до новой current branch автоматически раскрывается, даже если соответствующие folders ранее были свернуты. После этого последующие refresh сохраняют новое состояние.

При открытии другого repository presentation session начинается заново: expansion state сбрасывается и применяется initial expansion policy нового repository.''')
replace_section(
    idd12,
    "## Architecture",
    "## Acceptance Criteria",
    '''Expansion state, selection continuity и hierarchy-guide metadata принадлежат presentation layer и не являются частью Git/domain state.

Repository Tree является долгоживущей presentation structure для открытого repository. Новый repository snapshot сначала преобразуется в complete desired logical tree со stable keys, после чего existing tree reconciles с ним:

- одинаковый logical key повторно использует тот же `RepositoryTreeNode`;
- изменившиеся presentation data обновляются на существующем node;
- новые/исчезнувшие/reordered nodes выражаются `Add` / `Remove` / `Move`;
- `_repositoryTreeRoots` и `RepositoryTreeNode.Children` не получают `Reset` при ordinary refresh;
- branch folders идентифицируются полным logical prefix, а remote folders также remote scope;
- stash identity основана на stable stash commit, а не на positional `stash@{n}`;
- worktree identity основана на stable worktree path.

Branch grouping и sorting строятся в desired tree и затем reconciles без `Clear() + Add everything`. Tag order остаётся existing source order; изменение порядка выполняется через `Move` с сохранением nodes.

Один применённый repository-state snapshot коалесцируется максимум в одну Repository Tree reconciliation. `HeadDisplay`, `CurrentOperation`, `IsBusy` и другие non-structural properties сами по себе не инициируют structural tree update.

Worktrees загружаются отдельным source и reconciles отдельно: stable `Worktrees` root и его children обновляются без перестройки Branches/Remotes/Tags/Stashes; association local branch ↔ worktree обновляется на existing local branch node.

Если selected logical node сохраняется после reconciliation, сохраняется тот же selected presentation object. При удалении selection fallback выбирается детерминированно внутри той же logical area: next sibling, previous sibling, surviving parent, затем ближайший surviving ancestor/root.

Полный rebuild допустим только на repository lifecycle transition (initial/open/close/change) или как явно диагностированный exceptional recovery после inconsistent presentation state. Ordinary branch/tag/stash/worktree/fetch/manual refresh не используют full rebuild fallback.

Current-branch decoration является presentation-only state, вычисляемым из существующего `IsCurrent`. Оно не должно устанавливать `TreeViewItem.IsSelected`, менять selected item или перехватывать pointer/focus interaction строки.''')
replace_exact(
    idd12,
    "- Ручное раскрытие или сворачивание сохраняется после repository refresh/rebuild.",
    "- Ручное раскрытие или сворачивание сохраняется после ordinary repository refresh/mutation без замены unchanged nodes.")
replace_exact(
    idd12,
    "- Local и remote branches остаются Git-semantically неизменными; меняется только presentation behavior.",
    "- Local и remote branches остаются Git-semantically неизменными; меняется только presentation behavior.\n- Unchanged logical nodes сохраняют reference identity; ordinary refresh не вызывает `Reset` root/children collections.\n- Create/delete branch, remote ref changes, tag reorder, stash reindex и worktree changes изменяют только затронутые nodes/subtrees.\n- Surviving stash сохраняет node identity при `stash@{1} -> stash@{0}`.\n- Selected surviving node сохраняет selection; удалённый node получает deterministic fallback в той же logical area.")


idd14 = ROOT / ".idd/intent/IDD-0014.spec-selected-commit-actions-and-refresh-stability.md"
replace_exact(
    idd14,
    "A repository-state snapshot is applied to observable presentation state as a logical transaction. Collection replacement must not emit one rebuild per item, and multiple repository-state collection notifications must be coalesced into one presentation rebuild.",
    "A repository-state snapshot is applied to observable presentation state as a logical transaction. Source ViewModel collections may still use bulk `Reset`, but their notifications are coalesced into at most one Repository Tree reconciliation for that snapshot. Existing logical Repository Tree nodes keep reference identity across ordinary refresh cycles, and tree collections express changes with `Add` / `Remove` / `Move` rather than `Reset`.")
replace_exact(
    idd14,
    "Repository tree expansion/session state must not be reset by ordinary refresh. A different repository may reset repository-specific navigation state; reopening the same repository as a refresh mechanism is forbidden.",
    "Repository tree expansion/session state and surviving selection must not be reset by ordinary refresh. Ordinary refresh reconciles a stable presentation tree instead of rebuilding it; a different repository may start a new repository-specific navigation session. Reopening the same repository as a refresh mechanism is forbidden.")
replace_exact(
    idd14,
    "Regression/contract coverage verifies that refresh no longer reopens a repository, working-tree mutations use state-only refresh, collection application is bulk/coalesced, selection is not cleared before stage/unstage, Cancel Commit remains side-effect free, and the commit context menu exposes the required actions.",
    "Regression/contract coverage verifies that refresh no longer reopens a repository, working-tree mutations use state-only refresh, repository-state collection application is bulk/coalesced into one tree reconciliation, unchanged tree nodes retain identity without collection `Reset`, selection is not cleared before stage/unstage, Cancel Commit remains side-effect free, and the commit context menu exposes the required actions.")

# The helper is deliberately one-shot. Remove both it and its workflow from the resulting commit.
(ROOT / ".github/workflows/apply-incremental-tree-patch.yml").unlink()
Path(__file__).unlink()
