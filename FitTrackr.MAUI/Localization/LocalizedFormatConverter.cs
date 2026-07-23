using System.Globalization;

namespace FitTrackr.MAUI.Localization
{
    /// <summary>
    /// XAML-only helper for binding scenarios that used to rely on a hardcoded
    /// Binding.StringFormat (e.g. "⏱ {0} dk"). The localized format string is looked up
    /// from the resx via the key passed as ConverterParameter, then applied with
    /// string.Format to the bound value.
    /// Usage: Text="{Binding SomeValue, Converter={StaticResource LocalizedFormatConverter}, ConverterParameter=SomeResxKey}"
    /// </summary>
    public class LocalizedFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = parameter as string ?? string.Empty;
            var format = LocalizationResourceManager.Instance[key];
            return string.Format(format, value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
