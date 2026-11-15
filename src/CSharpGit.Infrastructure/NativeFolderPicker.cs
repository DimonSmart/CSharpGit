using CSharpGit.Application.Abstractions;
using Windows.Storage.Pickers;

namespace CSharpGit.Infrastructure;

public sealed class NativeFolderPicker : IFolderPicker
{
    private readonly Func<nint> _ownerWindowHandle;

    public NativeFolderPicker(Func<nint> ownerWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(ownerWindowHandle);
        _ownerWindowHandle = ownerWindowHandle;
    }

    public async Task<string?> PickFileAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _ownerWindowHandle());
        var file = await picker.PickSingleFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path;
    }

    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _ownerWindowHandle());
        var folder = await picker.PickSingleFolderAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return folder?.Path;
    }
}
