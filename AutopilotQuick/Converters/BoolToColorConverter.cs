#region

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

#endregion

namespace AutopilotQuick.Converters
{
    public class BoolToColorConverter : IValueConverter
    {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((bool)value)
            {
                return new SolidColorBrush(Color.FromRgb(0, 255, 0));
            }
            return new SolidColorBrush(Color.FromRgb(255, 0, 0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
