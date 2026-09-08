namespace CSharpGit.Application.Abstractions;

public interface IFolderPicker
{
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}
