namespace CSharpGit.Application.Abstractions;

public interface IFolderPicker
{
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);

    Task<string?> PickFileAsync(CancellationToken cancellationToken = default);
}
