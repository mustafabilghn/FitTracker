using Microsoft.Maui.Controls.Xaml;

namespace FitTrackr.MAUI.Localization
{
    /// <summary>
    /// XAML'de {loc:Translate Key=SomeKey} şeklinde kullanılır. LocalizationResourceManager'ın
    /// indexer'ına bir Binding kurar, böylece dil değiştiğinde (PropertyChanged("Item[]")) metin
    /// otomatik yenilenir.
    /// </summary>
    [ContentProperty(nameof(Key))]
    [AcceptEmptyServiceProvider]
    public class TranslateExtension : IMarkupExtension<BindingBase>
    {
        public string Key { get; set; } = string.Empty;

        public BindingBase ProvideValue(IServiceProvider serviceProvider)
        {
            return new Binding
            {
                Mode = BindingMode.OneWay,
                Path = $"[{Key}]",
                Source = LocalizationResourceManager.Instance
            };
        }

        object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
    }
}
