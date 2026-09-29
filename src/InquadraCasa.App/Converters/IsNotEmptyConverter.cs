using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace InquadraCasa.App.Converters;

/// <summary>Vero se la stringa non è vuota: per mostrare etichette solo quando hanno contenuto.</summary>
public sealed class IsNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
