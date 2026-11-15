# Git Tools / merge — manual GUI smoke test

Status: **Not executed**. The GitHub Actions matrix builds and runs CLI tests without opening real GUI applications. No manual GUI test may be reported as passed until a person runs it on the named OS.

Run separately on **Windows** (WinMerge or KDiff3) and **macOS** (KDiff3 or another installed merge tool):

1. Open Settings → Git Tools → Merge Tool; choose a preset and Global or This repository. Browse to the executable (test once with an executable outside PATH and in a path containing spaces), Save, and confirm Effective/Source; expand Advanced to inspect origin and overrides.
2. Click Test Merge Tool. The tool must open on an isolated conflict without altering the current repository or persistent Git config; close/cancel and check diagnostics.
3. In a disposable Git repository, create a real text conflict by editing and committing the same line on two branches, then merging. Open the selected conflicting file in the configured tool, resolve it, save and return to CSharpGit.
4. Confirm Git index status agrees with the UI. Stage resolution as needed; Continue the operation. Verify the resulting commit, re-open a still-conflicted repository after restarting CSharpGit, and check Abort/Skip availability.
5. Repeat for add/add, modify/delete, delete/modify and binary conflicts, plus a rebase. Check current/incoming labels and context actions against Git's ours/theirs meaning.
6. Cancel without resolving; the UI must not claim success or Continue automatically. Test an absent executable, an invalid path and a custom tool that cannot confirm success without prompting.
7. Keep Git Tools Settings open with unsaved changes, change the active repository, verify stale warning and disabled Save/Remove, then explicitly discard/reload. Test scope switching with two unsaved drafts.
8. Check layout at minimum supported window size and in Light/Dark themes, with keyboard navigation and accessible action labels.

Record a dated pass/fail/blocked result **separately for each OS and scenario**. Do not use ordinary CI success as evidence for these GUI checks.
