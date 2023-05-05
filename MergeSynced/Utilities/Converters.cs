using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MergeSynced.Utilities
{
    public class BoolToStatusColorConverter : IValueConverter
    {
        public static readonly BoolToStatusColorConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool statusOk && targetType.IsAssignableTo(typeof(Avalonia.Media.IBrush)))
            {
                return statusOk ? new SolidColorBrush(Colors.LimeGreen) : new SolidColorBrush(Colors.Red);
            }
            // converter used for the wrong type
            return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
