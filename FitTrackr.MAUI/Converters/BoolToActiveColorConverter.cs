using System.Globalization;

namespace FitTrackr.MAUI.Converters
{
    /// <summary>Dil seçici butonlarında seçili olanı vurgulamak için: true -> turuncu, false -> koyu gri.</summary>
    public class BoolToActiveColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? Color.FromArgb("#FF7043") : Color.FromArgb("#1C1C1C");
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
