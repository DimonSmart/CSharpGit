# History and repository startup performance

This note records the deterministic before/after command and hot-path structure for the
2026-09 history/repository performance work. Wall-clock and CPU timings are intentionally
reported by the desktop lifecycle check rather than asserted in CI because they depend on
the desktop environment.

## History scrolling

Before the change, every realized/recycled history row entered
`HistoryList_ContainerContentChanging` and queued two recursive visual-tree traversals:

- graph layout propagation;
- author-avatar service configuration.

After the change:

- `ContainerContentChanging` is not used by the history list;
- graph layout is published through `CommitGraphPresentationContext`;
- `AuthorAvatar` obtains the shared presentation service context on control lifetime;
- recursive graph-layout traversals: **0**;
- recursive avatar-configuration traversals: **0**.

`RunCommitGraphViewportLifecycleCheckAsync` now loads at least 300 rows, performs repeated
far scrolling/recycling, resize, load-more and return-to-top checks. It writes one trace
record with:

- scripted-scroll wall-clock time;
- delta `Process.TotalProcessorTime`;
- geometry update attempts;
- real geometry rebuilds;
- created graph controls and avatars;
- explicit avatar configurations;
- recursive traversal counters.

The timing values are engineering measurements only; structural zero-traversal assertions
remain deterministic.

## Repository cold open

Benchmark scenario: clean local repository, normal branch, one remote, valid local
`<remote>/HEAD`, no active operation/conflict, no reflog, no history filter, first history
page, no network operation.

The pre-change command path can be counted directly from the previous implementation:

| Phase | Before | After |
| --- | ---: | ---: |
| repository discovery + Git validation | 4 | 2 |
| preliminary refresh fingerprint | 8 | 0 |
| repository state + default branch + tags | 14 | 6 |
| first all-references history page | 1 | 1 |
| **total Git processes** | **27** | **9** |
| network Git processes | **0** (with valid local remote HEAD) | **0** |

The after count is enforced as an upper bound (`<= 10`) by
`RepositoryStartupCommandBudgetTests`. The expected clean-path count is 9, with the first
cold `git --version` included. A second open using the same
`GitRepositoryCommandRunner` must not run `git --version` again.

The command-budget test also verifies absence of:

- duplicate status, branch/ref, tag and stash scans;
- per-remote `remote get-url` calls;
- separate HEAD `symbolic-ref` / `rev-parse` during state read;
- `ls-remote`, fetch, pull or push in startup;
- history HEAD probes when the already-read state says the branch is unborn.

Per-command duration remains available from `GitCommandActivityHistory`; the executor
also emits command duration to `Trace`. This keeps production Git Console behavior
unchanged while allowing environment-specific wall-clock and summed Git-process timing
to be collected from the same fixture.
