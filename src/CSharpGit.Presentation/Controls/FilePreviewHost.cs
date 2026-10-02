using CSharpGit.Presentation.Previewing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Controls;

internal sealed class FilePreviewHost : Grid
{
    internal FilePreviewHost()
    {
        MinWidth = 250;
        ShowNothingSelected();
    }

    internal void ShowNothingSelected() =>
        SetContent(BuildMessage("Select a file to preview."));

    internal void ShowFolder() =>
        SetContent(BuildMessage("Select a file to preview."));

    internal void ShowLoading(string path)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new ProgressRing
        {
            Width = 18,
            Height = 18,
            IsActive = true
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Loading {Path.GetFileName(path)}…",
            VerticalAlignment = VerticalAlignment.Center
        });
        SetContent(panel);
    }

    internal void ShowUnsupported(string message) =>
        SetContent(BuildMessage(message));

    internal void ShowError(string message) =>
        SetContent(BuildMessage($"Preview unavailable\n{message}"));

    internal void Show(FilePreviewContent content)
    {
        SetContent(content switch
        {
            TextPreviewContent text => BuildText(text),
            ImagePreviewContent image => BuildImage(image),
            BinaryPreviewContent binary => BuildBinary(binary),
            _ => BuildMessage("Preview is not available.")
        });
    }

    private UIElement BuildText(TextPreviewContent content)
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        if (content.IsTruncated)
        {
            var notice = new TextBlock
            {
                Text = $"Preview truncated — file is {ImagePreviewFormatting.FormatSize(content.FileSize)}",
                Margin = new Thickness(8, 6, 8, 6),
                Opacity = 0.72
            };
            layout.Children.Add(notice);
        }

        var text = new TextBlock
        {
            Text = content.Text,
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = true,
            FontFamily = new FontFamily("Courier New"),
            Margin = new Thickness(8, 6, 8, 8)
        };
        var scroller = new ScrollViewer
        {
            Content = text,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollMode = ScrollMode.Auto
        };
        Grid.SetRow(scroller, 1);
        layout.Children.Add(scroller);
        return layout;
    }

    private static UIElement BuildImage(ImagePreviewContent content)
    {
        var view = new ImagePreviewView
        {
            Margin = new Thickness(8)
        };
        view.Show(content);
        return view;
    }

    private static UIElement BuildBinary(BinaryPreviewContent content)
    {
        var panel = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Binary file",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = content.Message,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            Opacity = 0.72
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Size: {ImagePreviewFormatting.FormatSize(content.FileSize)}",
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.72
        });
        return panel;
    }

    private static UIElement BuildMessage(string text) => new TextBlock
    {
        Text = text,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(16),
        Opacity = 0.72
    };

    private void SetContent(UIElement content)
    {
        Children.Clear();
        Children.Add(content);
    }
}
