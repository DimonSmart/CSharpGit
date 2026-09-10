from pathlib import Path


def replace_exact(path: str, old: str, new: str) -> None:
    file = Path(path)
    raw = file.read_bytes().decode("utf-8")
    eol = "\r\n" if "\r\n" in raw else "\n"
    text = raw.replace("\r\n", "\n")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one match in {path}, found {count}: {old[:120]!r}")
    text = text.replace(old, new)
    if eol == "\r\n":
        text = text.replace("\n", "\r\n")
    file.write_bytes(text.encode("utf-8"))


replace_exact(
    "src/CSharpGit.Git/GitFileAwareHistoryService.cs",
    """    public Task<HistoryPage> ReadHistoryAsync(\n        Repository repository,\n        HistoryQuery query,\n        CancellationToken cancellationToken = default) =>\n        _history.ReadHistoryAsync(repository, query, cancellationToken);\n\n""",
    """    public Task<HistoryPage> ReadHistoryAsync(\n        Repository repository,\n        HistoryQuery query,\n        CancellationToken cancellationToken = default) =>\n        _history.ReadHistoryAsync(repository, query, cancellationToken);\n\n    public Task<HistoryPage> ReadHistoryThroughCommitAsync(\n        Repository repository,\n        HistoryScope scope,\n        string targetHash,\n        int trailingCount = 100,\n        CancellationToken cancellationToken = default) =>\n        _history.ReadHistoryThroughCommitAsync(repository, scope, targetHash, trailingCount, cancellationToken);\n\n""",
)

replace_exact(
    "src/CSharpGit.Git/GitCliRepositoryService.cs",
    """    public GitCliRepositoryService(GitCliOptions options)\n    {\n        ArgumentNullException.ThrowIfNull(options);\n        _gitExecutable = string.IsNullOrWhiteSpace(options.ExecutablePath) ? \"git\" : options.ExecutablePath;\n    }\n\n""",
    """    public GitCliRepositoryService(GitCliOptions options)\n    {\n        ArgumentNullException.ThrowIfNull(options);\n        _gitExecutable = string.IsNullOrWhiteSpace(options.ExecutablePath) ? \"git\" : options.ExecutablePath;\n    }\n\n    public Task<HistoryPage> ReadHistoryThroughCommitAsync(\n        Repository repository,\n        HistoryScope scope,\n        string targetHash,\n        int trailingCount = 100,\n        CancellationToken cancellationToken = default) =>\n        new GitReferenceHistoryService(new GitCliOptions { ExecutablePath = _gitExecutable })\n            .ReadHistoryThroughCommitAsync(repository, scope, targetHash, trailingCount, cancellationToken);\n\n""",
)

print("History interface implementations updated.")
