using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private async void ChangeInteractiveRebaseAuthor_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement target
            || FindNamedDescendant<InteractiveRebaseTodoEditor>(
                InteractiveRebaseDialog,
                "InteractiveRebaseTodoEditor") is not { } editor)
        {
            return;
        }

        var todoText = editor.Text;
        var selectionStart = editor.SelectionStart;
        var selectionLength = editor.SelectionLength;

        InteractiveRebaseAuthorChangeAnalysis analysis;
        try
        {
            analysis = _viewModel.AnalyzeInteractiveRebaseAuthorChange(
                todoText,
                selectionStart,
                selectionLength);
        }
        catch (Exception exception)
        {
            ShowChangeAuthorErrorFlyout(target, exception.Message);
            return;
        }

        string? identityFailure = null;
        CSharpGit.Domain.RepositoryIdentitySnapshot? identity = null;
        try
        {
            identity = await _viewModel.ReadInteractiveRebaseAuthorIdentityAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            identityFailure = exception.Message;
        }

        var effectiveName = identity?.Name.EffectiveValue?.Trim();
        var effectiveEmail = identity?.Email.EffectiveValue?.Trim();
        var resetAvailable =
            !string.IsNullOrWhiteSpace(effectiveName)
            && !string.IsNullOrWhiteSpace(effectiveEmail);

        var resetMode = new RadioButton
        {
            Content = "Reset to current Git identity",
            GroupName = "ChangeAuthorSource",
            IsChecked = resetAvailable,
            IsEnabled = resetAvailable
        };
        var identityText = new TextBlock
        {
            Text = resetAvailable
                ? $"{effectiveName} <{effectiveEmail}>"
                : "Git identity is incomplete. Configure user.name and user.email for this repository or globally before resetting commit authors.",
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(28, 0, 0, 0)
        };

        var explicitMode = new RadioButton
        {
            Content = "Set author explicitly",
            GroupName = "ChangeAuthorSource",
            IsChecked = !resetAvailable
        };
        var nameBox = new TextBox
        {
            Header = "Name",
            MinWidth = 360,
            IsEnabled = !resetAvailable
        };
        var emailBox = new TextBox
        {
            Header = "Email",
            MinWidth = 360,
            IsEnabled = !resetAvailable
        };

        var resetAuthorDate = new CheckBox
        {
            Content = "Reset author date as well",
            IsChecked = false
        };

        var selectedScope = new RadioButton
        {
            Content = $"Selected commit lines ({analysis.SelectedEligibleCount})",
            GroupName = "ChangeAuthorScope",
            IsEnabled = analysis.SelectedEligibleCount > 0,
            IsChecked = analysis.SelectedEligibleCount > 0
        };
        var allScope = new RadioButton
        {
            Content = $"All eligible commits in this rebase ({analysis.AllEligibleCount})",
            GroupName = "ChangeAuthorScope",
            IsEnabled = analysis.AllEligibleCount > 0,
            IsChecked = analysis.SelectedEligibleCount == 0 && analysis.AllEligibleCount > 0
        };

        var scopeInfo = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };
        var validationError = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var apply = new Button
        {
            Content = "Apply"
        };
        var cancel = new Button
        {
            Content = "Cancel"
        };

        var authorFields = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(28, 0, 0, 0)
        };
        authorFields.Children.Add(nameBox);
        authorFields.Children.Add(emailBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);

        var content = new StackPanel
        {
            Width = 520,
            Spacing = 10
        };
        content.Children.Add(new TextBlock
        {
            Text = "Change author",
            FontSize = 20
        });
        content.Children.Add(new TextBlock
        {
            Text = "Author"
        });
        content.Children.Add(resetMode);
        content.Children.Add(identityText);
        if (!string.IsNullOrWhiteSpace(identityFailure))
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Git identity could not be read: {identityFailure}",
                TextWrapping = TextWrapping.Wrap
            });
        }

        content.Children.Add(explicitMode);
        content.Children.Add(authorFields);
        content.Children.Add(resetAuthorDate);
        content.Children.Add(new TextBlock
        {
            Text = "Apply to",
            Margin = new Thickness(0, 6, 0, 0)
        });
        content.Children.Add(selectedScope);
        content.Children.Add(allScope);
        content.Children.Add(scopeInfo);
        content.Children.Add(new TextBlock
        {
            Text =
                "Changing authors rewrites Git history.\n" +
                "Commit hashes may change from the first modified commit onward.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        content.Children.Add(validationError);
        content.Children.Add(buttons);

        var flyout = new Flyout
        {
            Content = content
        };

        void UpdateAuthorMode()
        {
            var explicitAuthor = explicitMode.IsChecked == true;
            nameBox.IsEnabled = explicitAuthor;
            emailBox.IsEnabled = explicitAuthor;
            UpdateApplyState();
        }

        void UpdateScopeInfo()
        {
            var selected = selectedScope.IsChecked == true;
            var eligible = selected
                ? analysis.SelectedEligibleCount
                : analysis.AllEligibleCount;
            var unsupported = selected
                ? analysis.SelectedUnsupportedCount
                : analysis.AllUnsupportedCount;

            scopeInfo.Text = unsupported > 0
                ? $"{eligible} eligible commits. {unsupported} commit rows in squash/fixup groups will be skipped."
                : $"{eligible} eligible commits.";
            UpdateApplyState();
        }

        void UpdateApplyState()
        {
            var scopeAvailable = selectedScope.IsChecked == true
                ? analysis.SelectedEligibleCount > 0
                : allScope.IsChecked == true
                  && analysis.AllEligibleCount > 0;
            var sourceAvailable = explicitMode.IsChecked == true || resetAvailable;
            apply.IsEnabled = scopeAvailable && sourceAvailable;
        }

        resetMode.Checked += (_, _) => UpdateAuthorMode();
        explicitMode.Checked += (_, _) => UpdateAuthorMode();
        selectedScope.Checked += (_, _) => UpdateScopeInfo();
        allScope.Checked += (_, _) => UpdateScopeInfo();
        cancel.Click += (_, _) => flyout.Hide();
        apply.Click += (_, _) =>
        {
            validationError.Visibility = Visibility.Collapsed;
            validationError.Text = string.Empty;

            try
            {
                var useCurrentIdentity = resetMode.IsChecked == true;
                var scope = selectedScope.IsChecked == true
                    ? InteractiveRebaseAuthorChangeScope.SelectedCommitLines
                    : InteractiveRebaseAuthorChangeScope.AllEligibleCommits;
                var result = _viewModel.ApplyInteractiveRebaseAuthorChange(
                    new InteractiveRebaseAuthorChangeRequest(
                        todoText,
                        selectionStart,
                        selectionLength,
                        scope,
                        useCurrentIdentity ? effectiveName! : nameBox.Text,
                        useCurrentIdentity ? effectiveEmail! : emailBox.Text,
                        resetAuthorDate.IsChecked == true));

                _viewModel.RebaseTodoText = result.TodoText;
                editor.Text = result.TodoText;
                flyout.Hide();
            }
            catch (ArgumentException exception)
            {
                validationError.Text = exception.Message;
                validationError.Visibility = Visibility.Visible;
            }
        };

        UpdateAuthorMode();
        UpdateScopeInfo();
        flyout.ShowAt(target);
    }

    private static void ShowChangeAuthorErrorFlyout(
        FrameworkElement target,
        string message)
    {
        var flyout = new Flyout
        {
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480,
                IsTextSelectionEnabled = true
            }
        };
        flyout.ShowAt(target);
    }
}
