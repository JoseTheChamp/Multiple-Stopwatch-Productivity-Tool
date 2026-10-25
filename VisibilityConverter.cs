using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MultiStopwatch;

/// <summary>
/// Visible when the value is true or a non-zero number; ConverterParameter="Invert" flips it.
/// </summary>
public sealed class VisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            bool flag => flag,
            int number => number != 0,
            null => false,
            _ => true,
        };
        if (parameter as string == "Invert")
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
