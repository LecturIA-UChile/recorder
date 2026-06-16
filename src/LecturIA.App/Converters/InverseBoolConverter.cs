using System.Globalization;
using System.Windows.Data;

namespace LecturIA.App.Converters;

/// <summary>
/// Returns the logical negation of a <see cref="bool"/> value.
/// Used to bind <c>ToolTipService.IsEnabled</c> to an inverse flag.
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
internal sealed class InverseBoolConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;
}
