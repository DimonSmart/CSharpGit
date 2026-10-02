using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;

namespace CSharpGit.Presentation.Previewing;

internal sealed class ImageDiffPreviewService
{
    private readonly IRepositoryFileVersionService _fileVersionService;
    private readonly ImageMetadataReader _metadataReader;
    private readonly DiffFileVersionPathResolver _pathResolver;

    internal ImageDiffPreviewService(
        IRepositoryFileVersionService fileVersionService,
        IRepositoryPathService repositoryPathService,
        ImageMetadataReader? metadataReader = null)
    {
        _fileVersionService = fileVersionService ?? throw new ArgumentNullException(nameof(fileVersionService));
        _metadataReader = metadataReader ?? SharedImageMetadataReader.Instance;
        _pathResolver = new DiffFileVersionPathResolver(
            fileVersionService,
            repositoryPathService ?? throw new ArgumentNullException(nameof(repositoryPathService)));
    }

    internal async Task<ImageDiffLoadResult> LoadCommitAsync(
        Repository repository,
        string commitHash,
        string selectedPath,
        CancellationToken cancellationToken)
    {
        var pair = await _fileVersionService.ResolveCommitAsync(
            repository,
            commitHash,
            selectedPath,
            cancellationToken);
        return await LoadResolvedAsync(repository, pair, cancellationToken);
    }

    internal async Task<ImageDiffLoadResult> LoadWorkingTreeAsync(
        Repository repository,
        WorkingTreeChange change,
        WorkingTreeDiffKind kind,
        CancellationToken cancellationToken)
    {
        var pair = await _fileVersionService.ResolveWorkingTreeAsync(
            repository,
            change,
            kind,
            cancellationToken);
        return await LoadResolvedAsync(repository, pair, cancellationToken);
    }

    private async Task<ImageDiffLoadResult> LoadResolvedAsync(
        Repository repository,
        DiffFileVersionPair pair,
        CancellationToken cancellationToken)
    {
        var original = await PrepareSideAsync(
            repository,
            pair.Original,
            DiffFileSide.Original,
            "Not present before this change",
            cancellationToken);
        var changed = await PrepareSideAsync(
            repository,
            pair.Changed,
            DiffFileSide.Changed,
            "Deleted by this change",
            cancellationToken);

        if (!original.IsImageCandidate || !changed.IsImageCandidate
            || original.Content.State == ImageDiffSideState.Missing
                && changed.Content.State == ImageDiffSideState.Missing)
            return new ImageDiffLoadResult(pair, null);

        ImageMetadataComparison? comparison = null;
        if (original.Content.Metadata is not null && changed.Content.Metadata is not null)
            comparison = ImageMetadataComparison.Compare(original.Content, changed.Content);

        return new ImageDiffLoadResult(
            pair,
            new ImageDiffPreviewContent(original.Content, changed.Content, comparison));
    }

    private async Task<PreparedImageSide> PrepareSideAsync(
        Repository repository,
        DiffFileVersion version,
        DiffFileSide side,
        string missingReason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (version.EntryKind == GitEntryKind.Missing)
        {
            return new PreparedImageSide(
                true,
                new ImageDiffSideContent(
                    ImageDiffSideState.Missing,
                    null,
                    0,
                    null,
                    null,
                    version.UnavailableReason ?? missingReason));
        }

        if (!version.CanOpen)
        {
            return new PreparedImageSide(
                false,
                new ImageDiffSideContent(
                    ImageDiffSideState.PreviewUnavailable,
                    null,
                    0,
                    null,
                    null,
                    version.UnavailableReason ?? "This file version is unavailable."));
        }

        var path = await _pathResolver.ResolveAsync(repository, version, side, cancellationToken);
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("The resolved file version no longer exists.", path);

        var metadata = await _metadataReader.ReadAsync(path, cancellationToken);
        if (metadata.Status == ImageMetadataReadStatus.Unsupported)
        {
            return new PreparedImageSide(
                false,
                new ImageDiffSideContent(
                    ImageDiffSideState.PreviewUnavailable,
                    path,
                    info.Length,
                    null,
                    null,
                    "This binary file is not a supported image."));
        }

        if (metadata.Status != ImageMetadataReadStatus.Success || metadata.Metadata is null)
        {
            return new PreparedImageSide(
                true,
                new ImageDiffSideContent(
                    ImageDiffSideState.PreviewUnavailable,
                    path,
                    info.Length,
                    null,
                    metadata.DetectedFormat,
                    "Preview unavailable because the image header is invalid or corrupt."));
        }

        if (!ImagePreviewSafety.CanRender(info.Length, metadata.Metadata, out var reason))
        {
            return new PreparedImageSide(
                true,
                new ImageDiffSideContent(
                    ImageDiffSideState.PreviewUnavailable,
                    path,
                    info.Length,
                    metadata.Metadata,
                    metadata.Metadata.Format,
                    reason));
        }

        return new PreparedImageSide(
            true,
            new ImageDiffSideContent(
                ImageDiffSideState.Image,
                path,
                info.Length,
                metadata.Metadata,
                metadata.Metadata.Format));
    }

    private sealed record PreparedImageSide(
        bool IsImageCandidate,
        ImageDiffSideContent Content);
}
