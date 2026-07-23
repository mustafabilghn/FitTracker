using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace FitTrackr.MAUI.Localization
{
    /// <summary>
    /// TR/EN dil metinlerine tek noktadan erişim. XAML tarafında <see cref="TranslateExtension"/>
    /// bu sınıfa bağlanır; dil değişince PropertyChanged("Item[]") ile bağlı tüm Label/Button
    /// metinleri uygulama yeniden başlatılmadan anında güncellenir.
    /// </summary>
    public sealed class LocalizationResourceManager : INotifyPropertyChanged
    {
        private static readonly Lazy<LocalizationResourceManager> LazyInstance = new(() => new LocalizationResourceManager());
        public static LocalizationResourceManager Instance => LazyInstance.Value;

        private readonly ResourceManager _resourceManager =
            new("FitTrackr.MAUI.Resources.Strings.AppResources", typeof(LocalizationResourceManager).Assembly);

        public CultureInfo CurrentCulture { get; private set; } = new CultureInfo("tr");

        private LocalizationResourceManager()
        {
        }

        public void SetCulture(CultureInfo culture)
        {
            CurrentCulture = culture;

            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        public string this[string key] => _resourceManager.GetString(key, CurrentCulture) ?? $"#{key}#";

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
