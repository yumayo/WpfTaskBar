using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WpfTaskBar
{
    public class BoolToThicknessConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? new Thickness(2) : new Thickness(0);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}