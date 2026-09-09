using CSharpGit.Domain;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace CSharpGit.Presentation.Controls;

public sealed class DiffLineKindToBrushConverter : IValueConverter
{
    private static readonly Brush AddedBrush = Brush(38, 46, 160, 67);
    private static readonly Brush RemovedBrush = Brush(38, 210, 48, 48);
    private static readonly Brush HeaderBrush = Brush(22, 128, 128, 128);
    private static readonly Brush TransparentBrush = Brush(0, 0, 0, 0);

    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        DiffLineKind.Added => AddedBrush,
        DiffLineKind.Removed => RemovedBrush,
        DiffLineKind.Header => HeaderBrush,
        _ => TransparentBrush
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static SolidColorBrush Brush(byte alpha, byte red, byte green, byte blue) =>
        new(ColorHelper.FromArgb(alpha, red, green, blue));
}
