using System.Diagnostics;
using System.Windows.Input;
using CSharpGit.Presentation.Threading;
using CSharpGit.Presentation.ViewModels;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CSharpGit.Presentation.Windowing;

/// <summary>Window-local UI boundary for command observation, messages and clipboard.</summary>
internal sealed class WindowUiOperations(
    Func<XamlRoot?> getXamlRoot,
    Func<bool> isShuttingDown,
    Func<ContentDialog, Task<ContentDialogResult>> showPageDialogAsync)
{
    internal async Task ShowInformationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = getXamlRoot(),
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await showPageDialogAsync(dialog);
    }

    internal void ReportUiFailure(Exception exception, string operation)
    {
        if (Microsoft.UI.Xaml.Application.Current is App app)
            app.LogUiFailure(exception, operation);
        else
            Trace.TraceError($"{operation} failed: {exception}");
    }

    internal void RunPageUiOperation(Func<Task> operation, string description, bool showError = false)
    {
        if (isShuttingDown()) return;
        _ = UiAsyncOperationRunner.RunAsync(
            operation,
            exception =>
            {
                ReportUiFailure(exception, description);
                if (showError && !isShuttingDown())
                    RunPageUiOperation(
                        () => ShowErrorAsync("Could not complete action", "The requested action could not be completed."),
                        "UI error notification");
            },
            () => isShuttingDown());
    }

    internal async Task ExecuteCommandAsync(ICommand command)
    {
        if (isShuttingDown()) return;
        // ExecuteAsync deliberately propagates; this XAML boundary owns observation.
        await UiAsyncOperationRunner.RunAsync(
            async () =>
            {
                if (command is AsyncCommand asyncCommand)
                    await asyncCommand.ExecuteAsync();
                else if (command.CanExecute(null))
                    command.Execute(null);
            },
            exception => ReportUiFailure(exception, "UI command"),
            () => isShuttingDown());
    }

    internal static Task CopyTextAsync(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        return Task.CompletedTask;
    }

    internal async Task ShowErrorAsync(string title, string message)
    {
        if (isShuttingDown() || getXamlRoot() is null) return;
        try
        {
            // Git progress uses CR to rewrite its current line. Preserve that
            // terminal behavior at the presentation boundary, not in Git results.
            var details = new TextBlock
            {
                Text = new GitOutputDisplayBuffer().Reset(message),
                TextWrapping = TextWrapping.NoWrap,
                IsTextSelectionEnabled = true
            };
            var dialog = new ContentDialog
            {
                XamlRoot = getXamlRoot(),
                Title = title,
                Content = new ScrollViewer
                {
                    Content = details,
                    MaxWidth = 720,
                    MaxHeight = 400,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close
            };
            await showPageDialogAsync(dialog);
        }
        catch (Exception exception)
        {
            // Showing an error can itself fail (concurrent dialog/disposed XamlRoot).
            ReportUiFailure(exception, "Error notification");
        }
    }


}
