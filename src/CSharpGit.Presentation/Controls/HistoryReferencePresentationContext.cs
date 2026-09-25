using System.Collections.Specialized;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Controls;

internal static class HistoryReferencePresentationContext
{
    private static OpenRepositoryViewModel? _viewModel;
    private static HashSet<string> _defaultRemoteBranches = new(StringComparer.Ordinal);

    internal static event EventHandler? Changed;

    internal static void Configure(OpenRepositoryViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (ReferenceEquals(_viewModel, viewModel))
        {
            RebuildDefaultRemoteBranches();
            return;
        }

        if (_viewModel is not null)
            _viewModel.RemoteBranches.CollectionChanged -= RemoteBranches_CollectionChanged;

        _viewModel = viewModel;
        _viewModel.RemoteBranches.CollectionChanged += RemoteBranches_CollectionChanged;
        RebuildDefaultRemoteBranches();
    }

    internal static bool IsDefaultRemoteBranch(string referenceName) =>
        Volatile.Read(ref _defaultRemoteBranches).Contains(referenceName);

    private static void RemoteBranches_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args) =>
        RebuildDefaultRemoteBranches();

    private static void RebuildDefaultRemoteBranches()
    {
        var viewModel = _viewModel;
        var next = viewModel is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : viewModel.RemoteBranches
                .Where(branch => branch.IsDefault)
                .Select(branch => branch.Name)
                .ToHashSet(StringComparer.Ordinal);

        Volatile.Write(ref _defaultRemoteBranches, next);
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
