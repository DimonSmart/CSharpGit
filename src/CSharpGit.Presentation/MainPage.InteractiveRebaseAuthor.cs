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
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var controlHeight = (double)resources["Height.Control"];
        var bodyFont = (double)resources["Font.Body"];
        var spacing = (double)resources["Spacing.M"];
        var compactButtonStyle = (Style)resources["CompactButtonStyle"];
        var compactTextBoxStyle = (Style)resources["CompactTextBoxStyle"];
        var compactCheckBoxStyle = (Style)resources["CompactCheckBoxStyle"];
        var titleTextStyle = (Style)resources["TitleTextStyle"];
        var bodyStrongTextStyle = (Style)resources["BodyStrongTextStyle"];
        var bodySubduedTextStyle = (Style)resources["BodySubduedTextStyle"];
        var secondaryTextStyle = (Style)resources["SecondaryTextStyle"];

        var resetMode = new RadioButton
        {
            Content = "Reset to current Git identity",
            GroupName = "ChangeAuthorSource",
            IsChecked = true,
            MinHeight = controlHeight,
            FontSize = bodyFont
        };
        var identityText = new TextBlock
        {
            Text = !string.IsNullOrWhiteSpace(effectiveName)
                   && !string.IsNullOrWhiteSpace(effectiveEmail)
                ? $"Current: {effectiveName} <{effectiveEmail}>"
                : identityFailure is null
                    ? "Git will resolve the current identity when rebase runs."
                    : "Current identity could not be read; Git will resolve it when rebase runs.",
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(controlHeight, 0, 0, 0),
            Style = bodySubduedTextStyle
        };

        var explicitMode = new RadioButton
        {
            Content = "Set author explicitly",
            GroupName = "ChangeAuthorSource",
            MinHeight = controlHeight,
            FontSize = bodyFont
        };
        var nameBox = new TextBox
        {
            Header = "Name",
            Style = compactTextBoxStyle
        };
        var emailBox = new TextBox
        {
            Header = "Email",
            Style = compactTextBoxStyle
        };

        var resetAuthorDate = new CheckBox
        {
            Content = "Reset author date as well",
            IsChecked = false,
            Style = compactCheckBoxStyle
        };

        var selectedScope = new RadioButton
        {
            Content = $"Selected commit lines ({analysis.SelectedEligibleCount})",
            GroupName = "ChangeAuthorScope",
            IsEnabled = analysis.SelectedEligibleCount > 0,
            IsChecked = analysis.SelectedEligibleCount > 0,
            MinHeight = controlHeight,
            FontSize = bodyFont
        };
        var allScope = new RadioButton
        {
            Content = $"All eligible commits ({analysis.AllEligibleCount})",
            GroupName = "ChangeAuthorScope",
            IsEnabled = analysis.AllEligibleCount > 0,
            IsChecked = analysis.SelectedEligibleCount == 0 && analysis.AllEligibleCount > 0,
            MinHeight = controlHeight,
            FontSize = bodyFont
        };

        var scopeInfo = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Style = secondaryTextStyle
        };
        var validationError = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Style = secondaryTextStyle
        };
        var apply = new Button
        {
            Content = "Apply",
            Style = compactButtonStyle
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Style = compactButtonStyle
        };

        var authorFields = new StackPanel
        {
            Spacing = spacing,
            Margin = new Thickness(controlHeight, 0, 0, 0),
            Visibility = Visibility.Collapsed
        };
        authorFields.Children.Add(nameBox);
        authorFields.Children.Add(emailBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = spacing
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);

        var body = new StackPanel
        {
            Spacing = spacing
        };
        body.Children.Add(resetMode);
        body.Children.Add(identityText);
        body.Children.Add(explicitMode);
        body.Children.Add(authorFields);
        body.Children.Add(resetAuthorDate);
        body.Children.Add(new TextBlock
        {
            Text = "Apply to",
            Margin = new Thickness(0, spacing, 0, 0),
            Style = bodyStrongTextStyle
        });
        body.Children.Add(selectedScope);
        body.Children.Add(allScope);
        body.Children.Add(scopeInfo);
        body.Children.Add(new TextBlock
        {
            Text = "Rewrites Git history; commit hashes may change from the first modified commit onward.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, spacing, 0, 0),
            Style = secondaryTextStyle
        });
        body.Children.Add(validationError);

        var content = new StackPanel
        {
            Width = 400,
            Spacing = spacing
        };
        content.Children.Add(new TextBlock
        {
            Text = "Change author",
            Style = titleTextStyle
        });
        content.Children.Add(new ScrollViewer
        {
            Content = body,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollMode = ScrollMode.Disabled
        });
        content.Children.Add(buttons);

        var flyout = new Flyout
        {
            Content = content
        };
        var rebaseDialog = InteractiveRebaseDialog;
        var startRebaseWasEnabled = rebaseDialog.IsPrimaryButtonEnabled;
        flyout.Closed += (_, _) =>
            rebaseDialog.IsPrimaryButtonEnabled = startRebaseWasEnabled;

        void UpdateAuthorMode()
        {
            var explicitAuthor = explicitMode.IsChecked == true;
            authorFields.Visibility = explicitAuthor
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateApplyState();
        }

        void UpdateScopeInfo()
        {
            var selected = selectedScope.IsChecked == true;
            var unsupported = selected
                ? analysis.SelectedUnsupportedCount
                : analysis.AllUnsupportedCount;

            scopeInfo.Text = unsupported > 0
                ? $"{unsupported} commit rows in squash/fixup groups will be skipped."
                : string.Empty;
            scopeInfo.Visibility = unsupported > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateApplyState();
        }

        void UpdateApplyState()
        {
            var scopeAvailable = selectedScope.IsChecked == true
                ? analysis.SelectedEligibleCount > 0
                : allScope.IsChecked == true
                  && analysis.AllEligibleCount > 0;
            var sourceAvailable = resetMode.IsChecked == true
                                  || explicitMode.IsChecked == true;
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
                        useCurrentIdentity ? string.Empty : nameBox.Text,
                        useCurrentIdentity ? string.Empty : emailBox.Text,
                        resetAuthorDate.IsChecked == true,
                        ResetToCurrentGitIdentity: useCurrentIdentity));

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
        rebaseDialog.IsPrimaryButtonEnabled = false;
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
