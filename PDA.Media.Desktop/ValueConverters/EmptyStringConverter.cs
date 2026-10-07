using System.Windows.Data;
using System.Globalization;
using System.Windows.Markup;

namespace PDA.Media.Desktop.ValueConverters
{
    public class EmptyStringConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? parameter : value;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
        public override object? ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}