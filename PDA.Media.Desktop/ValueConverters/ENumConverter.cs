using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Data;

namespace PDA.Media.Desktop.ValueConverters
{
    public class ENumConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null) return DependencyProperty.UnsetValue;
            return GetDescription((Enum)value);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value;
        }

        public static string GetDescription(Enum en)
        {
            Type type = en.GetType();
            MemberInfo[] memberInfo = type.GetMember(en.ToString());
            if (memberInfo is not null && memberInfo.Length > 0)
            {
                object[] attributes = memberInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);
                if (attributes is not null && attributes.Length > 0)
                {
                    return ((DescriptionAttribute)attributes[0]).Description;
                }
            }

            return en.ToString();
        }
    }
}