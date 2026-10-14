# Git diagnostic output redaction

The Git Command Activity history and Git Console are a diagnostic boundary.
Command-line arguments and completed stdout/stderr records are sanitized for
HTTP(S) URLs containing `user:password@`, `Authorization: Bearer ...`,
`Authorization: Basic ...`, and assignments named `password`, `token`,
`access_token`, `access-token`, or `credential` (with optional `--`).

Diagnostic output is buffered until CR, LF, NUL, or command completion, so a
credential split across read chunks is never published in part. An unterminated
record over 8192 UTF-16 code units suppresses the remaining diagnostic stream
and is marked as truncated. The per-stream retained output limit remains 1 MiB.

Limitations: this is *pattern-based* redaction, not an assurance that every
secret format can be detected. Unlabelled tokens, private keys, custom
authentication headers, arbitrary binary formats and values split across logical
record delimiters are not guaranteed to be masked. Commands known to produce
arbitrary credentials should use the existing sensitive-output API to suppress
their diagnostic streams entirely.

This policy applies only to diagnostics. Git process stdout/stderr returned to
parsers and the binary output file path are unchanged.
