# IDD Intent Index

This index helps humans and Coding Agents find relevant current intent documents.
It is not the source of truth.

Current `IDD-NNNN` documents directly under `.idd/intent/` contain normative
product intent, ADRs, or active spikes.

`GLOSSARY.md`, when present, is an optional unnumbered vocabulary support file
and is not listed in this index.

Git history is the source for deleted or previous document versions.

## Current documents

The `Document` column contains stable `IDD-NNNN` identifiers only. Do not put
filenames, file paths, or Markdown links in this column. Resolve an identifier to
the unique current `.idd/intent/IDD-NNNN.*.md` file when the document must be
opened.

| Document | Role | Area | Notes | Replaces |
| --- | --- | --- | --- | --- |
| IDD-0001 | Spec | Product foundation | Product scope, platforms, application model, behavioral reference | — |
| IDD-0002 | ADR | Desktop UI architecture | Accepted .NET, Uno, Skia, XAML, Fluent and MVVM stack | — |
| IDD-0003 | Spec | Repository state | Opening, worktrees, refresh, Git source of truth and integration | — |
| IDD-0004 | Spec | History and diff | Commit history, graph, details and diff viewing | — |
| IDD-0005 | Spec | Working tree and commits | File-level staging, commit, amend and discard | — |
| IDD-0006 | Spec | Refs and remotes | Branches, tags, remotes, upstream and authentication | — |
| IDD-0007 | Spec | Git workflows | Stash, merge and rebase | — |
| IDD-0008 | Spec | Conflicts and mergetool | Conflict resolution and recoverable in-progress operations | — |
| IDD-0009 | Spec | Desktop experience | Main window, responsiveness, concurrency, appearance and errors | — |
