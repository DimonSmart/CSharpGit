# Settings multi-process persistence

CSharpGit may run more than once (one repository/worktree per process), but all
instances normally use the same `settings.json`.

## Update transaction

1. Serialize simultaneous updates inside this instance with `_updateGate`.
2. Acquire the stable exclusive `settings.json.lock` file (15-second retry
   limit; cancellation stops waiting).
3. Load and normalize the **current on-disk** JSON under that lock.
4. Apply only the requested setting/group/recent-repository operation.
5. When needed, write a unique `settings.json.<guid>.tmp` file and atomically
   rename it over `settings.json`.
6. Publish the committed in-memory snapshot, release both locks, then raise
   `Changed` outside the locks.

The lock file must remain on disk after releasing the lease. Deleting the
lock file would allow a waiting process to lock an unlinked file while another
writer locks a newly created lock path.

Distinct settings written by independent instances are preserved. Concurrent
updates to the same property use last-successful-writer-wins semantics. A
no-op operation by a stale instance updates its local snapshot without writing
a new file and raises `Changed` when effective values changed externally.

## Failure and compatibility semantics

- Files are replaced only after all candidate bytes have been successfully
  written. Cancellation before rename and write/rename errors preserve the
  previously committed JSON and this instance's last committed snapshot.
- Temporary files have unique names and are cleaned up best-effort.
- Startup still tolerates malformed JSON and falls back to defaults. Updates
  require valid on-disk JSON and refuse to overwrite malformed/unreadable data.
- Existing setting format and all four settings facet interfaces are unchanged.
- Idle processes **do not** receive instant notifications when a different
  process writes settings; their next update will refresh and merge state.
- `FileShare.None` coordinates cooperating processes on local filesystems.
  External programs writing without the lock and network filesystems with
  unreliable locking semantics remain outside the guarantee.

## Tests

`JsonAppSettingsMultiInstanceTests` checks disjoint concurrent updates,
multiple independent settings instances creating repository groups, pinned/recent
state preservation, stale no-op refresh, cancellation under a held lock,
and malformed JSON safety.

`JsonAppSettingsServiceTests` and
`JsonAppSettingsRecentRepositoriesTests` retain rollback, no-op,
serialization/migration and atomic temporary-file cleanup checks.
