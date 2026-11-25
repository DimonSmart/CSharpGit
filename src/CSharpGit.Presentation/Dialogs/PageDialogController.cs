using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Presentation.Dialogs;

// Owns page-scoped modal serialization, cancellation and rebase editor layout.
internal sealed class PageDialogController(Page page, OpenRepositoryViewModel viewModel, Func<bool> isShuttingDown)
{
    private readonly Page _page = page;
    private readonly OpenRepositoryViewModel _viewModel = viewModel;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;
    private ContentDialog InteractiveRebaseDialog => GetDialog("InteractiveRebaseDialog");
    private readonly UiPageDialogGate _pageDialogGate = new();
    private readonly CancellationTokenSource _pageDialogLifetime = new();

    private ContentDialog? _activeDialog;
    private TaskCompletionSource? _activeDialogCompletion;

    internal void Shutdown()
    {
        _pageDialogLifetime.Cancel();
        _activeDialog?.Hide();
    }

    internal async Task DismissActiveDialogForDesktopCheckAsync()
    {
        var completion = _activeDialogCompletion;
        if (completion is null) return;
        _activeDialog?.Hide();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    internal Task<ContentDialogResult> ShowAsync(ContentDialog dialog) =>
        _pageDialogGate.ExecuteAsync(
            async () =>
            {
                if (_isShuttingDown() || _page.XamlRoot is null)
                    return ContentDialogResult.None;

                dialog.XamlRoot = _page.XamlRoot;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _activeDialog = dialog;
                _activeDialogCompletion = completion;
                try { return await dialog.ShowAsync(); }
                finally
                {
                    _activeDialog = null;
                    _activeDialogCompletion = null;
                    completion.TrySetResult();
                }
            },
            ContentDialogResult.None,
            _pageDialogLifetime.Token);



    internal async Task ShowInteractiveRebaseEditorAsync()
    {
        var dialog = InteractiveRebaseDialog;

        void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            if (FindNamedDescendant<InteractiveRebaseTodoEditor>(
                    sender,
                    "InteractiveRebaseTodoEditor") is not { } editor)
                return;

            var rootSize = sender.XamlRoot?.Size;
            var viewportWidth = rootSize?.Width ?? _page.ActualWidth;
            var viewportHeight = rootSize?.Height ?? _page.ActualHeight;

            var availableDialogWidth = Math.Max(320d, viewportWidth - 96d);
            var dialogWidth = Math.Min(1200d, availableDialogWidth);
            dialog.MaxWidth = dialogWidth;
            dialog.MinWidth = Math.Min(900d, dialogWidth);
            editor.Width = Math.Max(240d, dialogWidth - 48d);

            var availableEditorHeight = Math.Max(220d, viewportHeight - 220d);
            editor.MinHeight = Math.Min(350d, availableEditorHeight);
            editor.MaxHeight = Math.Min(
                650d,
                Math.Max(editor.MinHeight, availableEditorHeight));
            editor.Height = Math.Min(600d, editor.MaxHeight);
            editor.FocusEditor();
        }

        var repository = _viewModel.Repository;
        dialog.Opened += Dialog_Opened;
        ContentDialogResult result;
        try
        {
            result = await ShowAsync(dialog);
        }
        finally
        {
            dialog.Opened -= Dialog_Opened;
        }

        if (result != ContentDialogResult.Primary || _isShuttingDown()
            || !ReferenceEquals(repository, _viewModel.Repository))
            return;

        if (FindNamedDescendant<InteractiveRebaseTodoEditor>(
                dialog,
                "InteractiveRebaseTodoEditor") is { } editor)
        {
            await _viewModel.InteractiveRebase.StartPreparedInteractiveRebaseAsync(editor.Text);
        }
    }

    internal static T? FindNamedDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        if (root is T element && string.Equals(element.Name, name, StringComparison.Ordinal))
            return element;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            if (FindNamedDescendant<T>(VisualTreeHelper.GetChild(root, index), name) is { } child)
                return child;
        }

        return null;
    }

    internal ContentDialog GetDialog(string key)
    {
        if (_page.Resources[key] is not ContentDialog dialog)
            throw new InvalidOperationException($"Page dialog resource '{key}' was not found.");

        dialog.DataContext = _viewModel;
        dialog.XamlRoot = _page.XamlRoot;
        return dialog;
    }
}
