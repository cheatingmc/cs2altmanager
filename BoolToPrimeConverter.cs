using System;
using System.Globalization;
using System.Windows.Data;

namespace CacheLoginToolWPF
{
    public class BoolToPrimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPrime)
            {
                return isPrime ? "True" : "False";
            }
            return "False";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                return str.Equals("True", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
    }
}

