using CSharpGit.Domain;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CSharpGit.Presentation.Controls;

public sealed class DiffLineKindToBrushConverter : IValueConverter
{
    private static readonly Brush AddedBrush = new SolidColorBrush(Color.FromArgb(38, 46, 160, 67));
    private static readonly Brush RemovedBrush = new SolidColorBrush(Color.FromArgb(38, 210, 48, 48));
    private static readonly Brush HeaderBrush = new SolidColorBrush(Color.FromArgb(22, 128, 128, 128));
    private static readonly Brush TransparentBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        DiffLineKind.Added => AddedBrush,
        DiffLineKind.Removed => RemovedBrush,
        DiffLineKind.Header => HeaderBrush,
        _ => TransparentBrush
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
