using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private async void CopyCommitText_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string text } button || string.IsNullOrEmpty(text)) return;

        await CopyTextAsync(text);

        if (button.Content is not FontIcon icon) return;
        var originalGlyph = icon.Glyph;
        icon.Glyph = "\uE73E";
        try
        {
            await Task.Delay(900);
        }
        finally
        {
            if (ReferenceEquals(button.Content, icon)) icon.Glyph = originalGlyph;
        }
    }
}
