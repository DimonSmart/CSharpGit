namespace CSharpGit.Presentation;

internal enum DiffPresentationState
{
    None,
    NothingSelected,
    LoadingDiff,
    LargeDiff,
    LoadingImage,
    Text,
    Image,
    OtherBinary,
    NoTextualPatch,
    NoNetStashDiff,
    DeltaMissing,
    Unavailable,
    Error
}
