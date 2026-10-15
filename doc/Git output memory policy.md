# Git process output memory policy

The Git executor drains stdout and stderr concurrently. Binary file export streams
stdout directly to its destination. For other calls, stdout is currently returned as a
complete string, so caller-specific limits and parsing strategies matter.

| Git output | Format / expected size | Current protection | Remaining risk |
|---|---|---|---|
| status --porcelain=v2 -z | NUL; proportional to changed file count | Lazy record parsing, no intermediate array of all records | Executor still retains the full text and the final string |
| for-each-ref | NUL + RS; proportional to ref count | Lazy record iteration | Complete stdout remains resident |
| config --null --list | NUL; proportional to config | Suppressed activity output; lazy config parsing | Complete stdout is required for snapshot |
| stash list | NUL + RS; proportional to stash count | Lazy record iteration | Complete stdout remains resident |
| history/log | paged commits; bounded by query | Per-query paging | Very large individual commit messages |
| diff / preview | text; proportional to patch size | Selected commands use maxStandardOutputBytes | Other call paths may remain unbounded |
| stderr | arbitrary text; potentially infinite | 4 MiB UTF-8 limit with process-tree kill | Very large legitimate stderr is rejected |
| binary exports | arbitrary binary | Stream copy to output file | Destination write errors are fatal |

Byte limits use an incremental UTF-8 encoder to count multi-code-unit symbols across
reader boundaries. Exceeding a limit is a hard failure: no partial result is returned.
The process tree is terminated and pending pumps are drained on failure or cancellation.
Protected Git config and remote diagnostics remain suppressed independently of limits.

Memory measurements should compare peak working set and allocated bytes for clean and
heavily modified repositories with 10k, 100k and 1M status entries, alongside histories
of 10k+ commits and diffs of 1 MiB, 20 MiB and 100 MiB. Run each case in a separate
process with identical runtime and GC settings; do not use a CI assertion on absolute
memory. This stage removes the largest record-splitting arrays but does **not** yet
replace the executor's complete stdout with streaming parsers.
