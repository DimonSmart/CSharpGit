using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CSharpGit.Presentation.Controls;

internal sealed class ImagePreviewView : Grid
{
    internal void Show(CSharpGit.Presentation.Previewing.ImagePreviewContent content) =>
        Show(
            content.LocalPath,
            content.FileSize,
            content.Metadata,
            content.DetectedFormat,
            content.CanRender,
            content.UnavailableReason);

    internal void Show(
        string? localPath,
        long fileSize,
        CSharpGit.Presentation.Previewing.ImageMetadata? metadata,
        CSharpGit.Presentation.Previewing.ImageFormat? detectedFormat,
        bool canRender,
        string? unavailableReason,
        string? missingMessage = null)
    {
        Children.Clear();
        RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var content = new Grid();
        if (localPath is null)
        {
            content.Children.Add(BuildMessage(missingMessage ?? "Image is not present."));
        }
        else if (!canRender)
        {
            content.Children.Add(BuildMessage(unavailableReason ?? "Preview unavailable"));
        }
        else
        {
            var image = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var error = BuildMessage("Preview unavailable");
            error.Visibility = Visibility.Collapsed;
            image.ImageFailed += (_, _) =>
            {
                image.Visibility = Visibility.Collapsed;
                error.Visibility = Visibility.Visible;
            };
            image.Source = new BitmapImage(new Uri(localPath, UriKind.Absolute));
            content.Children.Add(image);
            content.Children.Add(error);
        }
        Children.Add(content);

        if (localPath is not null)
        {
            var metadataText = new TextBlock
            {
                Text = ImagePreviewFormatting.FormatMetadata(metadata, detectedFormat, fileSize),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Opacity = 0.72,
                Margin = new Thickness(4, 6, 4, 0)
            };
            Grid.SetRow(metadataText, 1);
            Children.Add(metadataText);
        }
    }

    private static TextBlock BuildMessage(string text) => new()
    {
        Text = text,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12),
        Opacity = 0.72
    };
}

internal static class ImagePreviewFormatting
{
    internal static string FormatMetadata(
        CSharpGit.Presentation.Previewing.ImageMetadata? metadata,
        CSharpGit.Presentation.Previewing.ImageFormat? detectedFormat,
        long fileSize)
    {
        var format = FormatImageFormat(metadata?.Format ?? detectedFormat);
        var dimensions = metadata is null ? null : $"{metadata.Width} × {metadata.Height}";
        return string.Join(" · ", new[] { format, dimensions, FormatSize(fileSize) }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    internal static string FormatImageFormat(CSharpGit.Presentation.Previewing.ImageFormat? format) => format switch
    {
        CSharpGit.Presentation.Previewing.ImageFormat.Png => "PNG",
        CSharpGit.Presentation.Previewing.ImageFormat.Jpeg => "JPEG",
        _ => "Image"
    };

    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}
