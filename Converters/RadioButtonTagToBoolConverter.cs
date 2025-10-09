using Microsoft.UI.Xaml.Data;
using System;

namespace WinExSpectrumTest.Converters
{
    public partial class RadioButtonTagToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null || parameter == null)
                return false;

            return value.ToString().Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isChecked && isChecked && parameter != null)
            {
                return parameter.ToString();
            }
            return Microsoft.UI.Xaml.DependencyProperty.UnsetValue;
        }
    }
}
