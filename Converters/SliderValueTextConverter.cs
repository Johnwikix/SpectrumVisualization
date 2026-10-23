using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;
using System.Globalization;

namespace WinExSpectrumTest.Converters;

/// <summary>Formats settings slider tooltips without exposing float-to-double precision tails.</summary>
public sealed partial class SliderValueTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is double number && double.IsFinite(number)
            ? number.ToString("0.##", CultureInfo.CurrentCulture)
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        DependencyProperty.UnsetValue;
}
