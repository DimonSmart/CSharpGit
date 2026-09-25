# History performance diagnostics

History performance capture is a focused diagnostic mode for investigating slow vertical scrolling in the History list. It is independent of ordinary file logging and does not require Trace logging.

The capture is designed to compare the same scrolling experiment on a small repository and a large repository without adding expensive diagnostics to the normal rendering path.

## Enable the mode

1. Open **Settings → Diagnostics**.
2. Turn on **Enable history performance diagnostics**.
3. Return to History.

The History toolbar shows a **Perf** menu while the setting is enabled. Merely enabling the setting does not start a capture and does not create output files.

## Run a capture

1. Open a repository and wait until History has finished loading.
2. Stay away from the lower edge of the currently loaded range so the test does not trigger paging.
3. Open **Perf → Start performance capture**.
4. Scroll an already loaded range up and down.
5. Use **Perf → Stop performance capture**.

Only one History performance session can run at a time. A session stops automatically after 10 minutes.

A capture also stops when the repository is closed or changed, the diagnostic setting is disabled, or the application shuts down.

## Output files

Captures are written under:

```text
<LocalApplicationData>/CSharpGit/Diagnostics/HistoryPerformance/
```

Each normal capture produces:

```text
history-perf-YYYYMMDD-HHMMSS-p<process>-<session>.jsonl
history-perf-YYYYMMDD-HHMMSS-p<process>-<session>.summary.txt
```

Use **Perf → Open diagnostics folder** to open the directory.

The JSONL file is structured telemetry. Every line is an independent JSON object and a normal stop ends with a `sessionSummary` record. Periodic aggregate snapshots are written approximately once per second. Slow operations at or above 8 ms are retained in a bounded buffer and written without blocking the UI thread.

The text summary is intended for first-pass comparison and bug reports.

## Privacy

History performance captures do not contain repository paths or names, remote URLs, branch or tag names, commit messages, commit SHAs, author names or emails, file contents, diffs, Git stdout/stderr, Git working directories, or complete Git arguments.

Git activity is recorded only as aggregate user/internal counts and privacy-safe command categories such as `status`, `log`, `for-each-ref`, `fetch`, `pull`, `push`, `clone`, and `ls-remote`.

## Reproducible small/large repository test

Use a Release build and keep the experiment as similar as practical for both repositories.

1. Open the first repository and wait until History is idle.
2. Use the same window size you will use for the second repository.
3. Do not stay near the bottom of the loaded History page.
4. Start a performance capture.
5. Scroll an already loaded range up and down for roughly the same amount of time and distance.
6. Do not switch repository, History mode, tabs, or settings during the run.
7. Stop the capture.
8. Repeat the same sequence for the second repository.

For a clean scrolling experiment, the summary should normally show zero page loads and zero Git commands, and it should not report a History mutation, window resize, or graph-layout publication.

## Reading the main metrics

### Rendering

Rendering callback intervals are an indicator of UI/render stalls. They are not reported as exact FPS or exact dropped frames. In particular, the raw `>16.7 ms` threshold does not imply a dropped frame on every display.

Compare p95, p99, maximum interval, and counts above 33, 50, and 100 ms.

### Virtualization

`Container changes` and `Recycle events` show how much ListView realization work occurred during the captured scroll. The temporary `ContainerContentChanging` subscription exists only while a capture is active and performs only O(1) accounting.

### Commit graph

Compare:

- graph controls created / loaded / unloaded;
- Measure and Arrange counts and duration distributions;
- geometry attempts, cache hits, and rebuilds;
- geometry rebuild reasons;
- geometry builder and XAML materialization duration;
- topology converter calls and duration.

Ordinary vertical scrolling should not cause graph-layout initialization or presentation publication.

### Graph layout

`InitializeGraphLayout` is the full-history scan used to establish the current lane width. During ordinary scrolling of an already loaded range it should remain zero.

`UpdateGraphLayout`, queued/applied layout updates, presentation publishes, and presentation deliveries reveal unexpected layout churn.

### History scans

The summary separately tracks known History-wide or index-lookup work that can run from presentation code. A large-repository capture with a similar call count but much larger total scan time is a strong signal of an O(N) dependency on History size.

### History scans

The capture distinguishes `HistoryGlobalScan`, `HistoryIndexLookup`, `CommitLookup`, `ParentLookup`, and `RefLookup`. Calls, duration distributions, and naturally known examined-item counts are recorded without adding extra scans just for diagnostics.

### Avatars and owned disk activity

Compare refreshes, resolve requests, cancellations, stale completions, applied results, and sync/async resolve completions. Repeated online avatar work while revisiting already loaded rows can be identified without storing author identity.

When the concrete avatar service exposes diagnostic activity, the capture also counts memory-cache hits/misses, disk-cache hits/misses, remote requests, remote bytes read, avatar-cache writes, and bytes written. Paths and identities are never included.

### Runtime

The capture records process-wide allocation delta, Gen0/Gen1/Gen2 collections, heap and working-set deltas, process CPU time, CPU/wall ratio, and normalized CPU percentage.

### Git

For a pure scroll of an already loaded range the expected Git command count is zero. Any command started during the capture is visible in the aggregate summary without arguments, paths, or command output.

## Capture integrity

The summary explicitly flags conditions that can invalidate a clean scrolling comparison:

- History mutated;
- page load occurred;
- repository changed;
- window resized;
- graph layout was published;
- Git command executed;
- settings changed;
- online avatar request executed.

These flags do not necessarily indicate application bugs. They explain what else happened during the experiment.

## Recommended bug report

Attach:

1. the small-repository `.summary.txt`;
2. the large-repository `.summary.txt`;
3. both `.jsonl` files if deeper timing analysis is required;
4. the CSharpGit version and which capture visibly felt slow.

The two summaries are normally sufficient to identify which hot-path category grows with repository or History size.
