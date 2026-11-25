using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace CSharpGit.Presentation.Controls;

public sealed class NonEmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        !string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
