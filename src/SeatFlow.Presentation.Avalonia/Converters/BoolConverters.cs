using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SeatFlow.Presentation.Avalonia.Converters;

public static class BoolConverters
{
    public static readonly IValueConverter TrueToVisible = new BoolToDoubleConverter(1 , 0);
    public static readonly IValueConverter FalseToVisible = new BoolToDoubleConverter(0 , 1);
    public static readonly IValueConverter Negate = new BoolInvertConverter();
    public static readonly IValueConverter CompactPanelWidth = new BoolToDoubleConverter(80 , double.NaN);
    public static readonly IValueConverter TrueWhenEqual = new EqualsConverter();
    public static readonly IValueConverter TrueToBold = new BoolToFontWeightConverter();

    private class BoolToFontWeightConverter : IValueConverter
    {
        public object? Convert (object? value , Type targetType , object? parameter , CultureInfo culture)
            => value is true ? global::Avalonia.Media.FontWeight.Bold : global::Avalonia.Media.FontWeight.Normal;

        public object? ConvertBack (object? value , Type targetType , object? parameter , CultureInfo culture)
            => throw new NotSupportedException();
    }

    private class BoolToDoubleConverter (double trueValue , double falseValue) : IValueConverter
    {
        public object? Convert (object? value , Type targetType , object? parameter , CultureInfo culture)
            => value is true ? trueValue : falseValue;

        public object? ConvertBack (object? value , Type targetType , object? parameter , CultureInfo culture)
            => throw new NotSupportedException();
    }

    private class BoolInvertConverter : IValueConverter
    {
        public object? Convert (object? value , Type targetType , object? parameter , CultureInfo culture)
            => value is bool b ? !b : value;

        public object? ConvertBack (object? value , Type targetType , object? parameter , CultureInfo culture)
            => value is bool b ? !b : value;
    }

    private class EqualsConverter : IValueConverter
    {
        public object? Convert (object? value , Type targetType , object? parameter , CultureInfo culture)
        {
            if (parameter is string s && int.TryParse(s , out var expected))
                return value is int v && v == expected;
            return false;
        }

        public object? ConvertBack (object? value , Type targetType , object? parameter , CultureInfo culture)
            => throw new NotSupportedException();
    }

}
