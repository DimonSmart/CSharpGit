# History and repository startup performance

This note records the deterministic before/after command and hot-path structure for the
2026-09 history/repository performance work. Wall-clock and CPU timings are intentionally
reported by the desktop lifecycle check rather than asserted in CI because they depend on
the desktop environment.

## History scrolling

### Commit graph rendering stalls

Reproduction used a Release desktop build, 500 already loaded commits, a fixed
1400x900 window, and 720 programmatic scroll steps over the same 0..271 index
range. Paging, Git commands, history mutations, window resizing, graph-layout
publication, and online avatar requests were all zero during each capture.

The original five-mode A/B capture showed that the large regression begins when
the commit graph is added:

| Rendering mode | Duration | p95 | max | >33 ms | >50 ms | >100 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| SubjectOnly | 17.9 s | 16 ms | 28.2 ms | 0 | 0 | 0 |
| TextColumns | 18.6 s | 33 ms | 43.3 ms | 4 | 0 | 0 |
| TextAndGraph | 26.2 s | 100 ms | 84.7 ms | 476 | 183 | 0 |
| TextGraphAndReferences | 29.9 s | 100 ms | 131.7 ms | 516 | 331 | 3 |
| Full | 42.1 s | 250 ms | 144.3 ms | 483 | 483 | 73 |

In `TextAndGraph`, 1485 row realizations caused only two geometry builds. The
215 cached geometry materializations took about 45 ms in total, while the
capture contained 476 long render intervals. `SizeChanged` also had no repeated
height changes: it reported 1485 first-valid-height events and eight width-only
events. Geometry calculation, XAML materialization, and `SizeChanged` therefore
could not account for the stalls.

The sampled UI-thread profile localized the missing time to Uno 6.7.135's Skia
damage-region processing:

```text
CompositionTarget.Render
  SkiaRenderHelper.RecordPictureAndReturnPath
    Compositor.RenderRootVisual
      Visual.Render / RenderChildrenStep
        Visual.ContributeDamageOnPaint
          DamageRegionExtensions.UnionRect / Union
            SKPath.Op
```

During the graph interval, `CompositionTarget.Render` accounted for about 17.0 s
of sampled UI-thread time and damage-region union work for about 10.7 s. Each
realized graph row contributed nine XAML `Path` visuals. When scrolling moved
those visuals, Uno repeatedly converted and unioned their stroked paths into the
frame's damage region. This cost is downstream of the application's geometry
cache and is why fast geometry-builder timings did not predict smooth frames.

`CommitGraphControl` now keeps the same cached `CommitGraphGeometry`, but renders
it through one `SKCanvasElement` per realized row. The surface is clipped by
Uno's `SKCanvasVisual`; it does not call `canvas.Clear`, because clearing the
shared compositor canvas was the cause of the historical hover/selection
artifact in the earlier canvas implementation.

The causal A/B run with the single drawing surface, using the same
`TextAndGraph` workload under the same sample profiler, changed the result as
follows:

| Metric | XAML Paths | Single surface |
| --- | ---: | ---: |
| Duration | 26.2 s | 22.7 s |
| p95 render interval | 100 ms | 50 ms |
| max render interval | 84.7 ms | 59.5 ms |
| intervals >33 ms | 476 | 271 |
| intervals >50 ms | 183 | 13 |
| intervals >100 ms | 0 | 0 |

The >50 ms stall count fell by 92.9%. The remaining cost is primarily the
managed ListView/text row and grows further when references and avatars are
enabled. The graph-specific long stalls no longer dominate the scroll path.

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

The presentation refresh monitor no longer schedules an unconditional probe immediately
after publishing that freshly-read baseline. It revalidates only when an external
invalidation/probe was already pending or running, or when a previously detected external
change had suspended the monitor. Therefore the normal clean open remains at the measured
service-path budget instead of immediately paying for a second state/probe cycle.

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
