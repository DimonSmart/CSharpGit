from pathlib import Path

path = Path("src/CSharpGit.Git/GitCliRepositoryService.cs")
text = path.read_text(encoding="utf-8")
old = "public sealed partial class GitCliRepositoryService : IRepositoryService, IRepositoryStateService, IHistoryService, IWorkingTreeService, IReferenceService, IRepositoryWorkflowService"
new = "public sealed partial class GitCliRepositoryService : IRepositoryService, IRepositoryStateService, IWorkingTreeService, IReferenceService, IRepositoryWorkflowService"
if text.count(old) != 1:
    raise RuntimeError(f"Expected one GitCliRepositoryService declaration, found {text.count(old)}")
path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")
