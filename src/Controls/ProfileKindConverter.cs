using System.Globalization;
using System.Windows.Data;

namespace SherpaManager.Controls;

public sealed class ProfileKindConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string)?.Trim().ToLowerInvariant() switch
        {
            "iracing" => "iRacing",
            "acc" or "assetto corsa competizione" => "ACC",
            "work" => "Work",
            _ => "Profile"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
