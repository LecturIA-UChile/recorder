using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LecturIA.App.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound
/// <see cref="bool"/> is <see langword="false"/>, and
/// <see cref="Visibility.Collapsed"/> when it is <see langword="true"/>.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
internal sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}
