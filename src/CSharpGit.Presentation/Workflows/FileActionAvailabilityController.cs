using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Workflows;

// Owns only action-button availability and tooltips; FileActionsViewModel owns the requests.
internal sealed class FileActionAvailabilityController(
    OpenRepositoryViewModel viewModel,
    FileActionsViewModel fileActions,
    FileActionAvailabilityController.Controls commit,
    FileActionAvailabilityController.Controls workingTree)
{
    internal readonly record struct Controls(Button Original, Button Changed, Button ExternalDiff, Button Reveal);
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly FileActionsViewModel _fileActions = fileActions;
    private readonly Controls _commit = commit;
    private readonly Controls _workingTree = workingTree;

    internal void UpdateCommitButtons()
    {
        var pair = _fileActions.Commit.Prepared?.Pair;
        if (pair is not null)
        {
            SetVersionButton(_commit.Original, pair.Original, "No original version is available.");
            SetVersionButton(_commit.Changed, pair.Changed, "No changed version is available.");
            SetExternalDiffButton(_commit.ExternalDiff, pair);
        }
        else
        {
            // Preserve the existing conservative/optimistic button state during loading.
            var file = _viewModel.CommitDetails.SelectedFile;
            SetAvailabilityButton(_commit.Original,
                file is not null && file.Status != "A", "Open original version",
                "No original version is available.");
            SetAvailabilityButton(_commit.Changed,
                file is not null && file.Status != "D", "Open changed version",
                "No changed version is available.");
            SetAvailabilityButton(_commit.ExternalDiff, file is not null,
                "Open this exact OLD/NEW pair in the configured external diff tool",
                "Select a file diff first.");
        }
        SetRevealButton(_commit.Reveal, _fileActions.Commit.Prepared?.RevealPath ?? _fileActions.Commit.RevealPath);
    }

    internal void UpdateWorkingTreeButtons()
    {
        var prepared = _fileActions.WorkingTree.Prepared;
        SetVersionButton(_workingTree.Original, prepared?.Pair.Original, "No original version is available.");
        SetVersionButton(_workingTree.Changed, prepared?.Pair.Changed, "No changed version is available.");
        SetExternalDiffButton(_workingTree.ExternalDiff, prepared?.Pair);
        SetRevealButton(_workingTree.Reveal, prepared?.RevealPath);
    }

    private static void SetAvailabilityButton(Button? button, bool enabled, string enabledText, string disabledText)
    {
        if (button is null) return;
        button.IsEnabled = enabled;
        ToolTipService.SetToolTip(button, enabled ? enabledText : disabledText);
    }

    private static void SetVersionButton(Button? button, DiffFileVersion? version, string fallbackReason)
    {
        if (button is null) return;
        button.IsEnabled = version?.CanOpen == true;
        ToolTipService.SetToolTip(button, version?.CanOpen == true
            ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)
            : version?.UnavailableReason ?? fallbackReason);
    }

    private static void SetExternalDiffButton(Button? button, DiffFileVersionPair? pair)
    {
        if (button is null) return;
        var enabled = pair is not null && FileActionsViewModel.CanExternalDiff(pair);
        button.IsEnabled = enabled;
        ToolTipService.SetToolTip(button, enabled
            ? "Open this exact OLD/NEW pair in the configured external diff tool"
            : FileActionsViewModel.ExternalDiffUnavailableReason(pair));
    }

    private void SetRevealButton(Button? button, string? path)
    {
        if (button is null) return;
        button.IsEnabled = path is not null;
        ToolTipService.SetToolTip(button, path is null
            ? "The file is not present in the current working tree."
            : _fileActions.RevealDescription);
    }


}
