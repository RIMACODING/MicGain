using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MicGain.App.Converters;

/// <summary>Visible when the bound value is <c>false</c>; collapsed otherwise.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
