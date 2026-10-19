using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Pegged.Core;

/// <summary>Turns a numeric Tag on a control into a uniform corner radius.</summary>
public sealed class TagToCornerRadiusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return new CornerRadius(8);
        var d = value switch
        {
            double v => v,
            int i => i,
            string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            _ => 8d,
        };
        return new CornerRadius(d);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is CornerRadius c ? c.TopLeft : (object)8d;
}

/// <summary>True when a value equals the converter parameter.</summary>
public sealed class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value, parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}
