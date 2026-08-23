using FitTrackr.MAUI.Localization;
using FitTrackr.MAUI.Pages;
using FitTrackr.MAUI.Services;
using System.Globalization;

namespace FitTrackr.MAUI
{
    public partial class App : Application
    {
        private readonly AppShell shell;
        private readonly AuthService authService;
        private readonly WorkoutService workoutService;

        public App(AppShell shell, AuthService authService, WorkoutService workoutService)
        {
            InitializeComponent();

            // Kullanıcı ProfilePage'den elle bir dil seçtiyse (Preferences.Set("app_language", "tr"|"en"))
            // o tercih her zaman önceliklidir. Elle bir seçim yoksa uygulama cihazın sistem diline göre
            // kendini otomatik ayarlar: cihaz Türkçe ise Türkçe, aksi halde (İngilizce dahil her dil) İngilizce.
            string languageCode;
            if (Preferences.ContainsKey("app_language"))
            {
                languageCode = Preferences.Get("app_language", "tr");
            }
            else
            {
                languageCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";
            }

            var appCulture = CultureInfo.GetCultureInfo(languageCode == "tr" ? "tr-TR" : "en-US");
            CultureInfo.DefaultThreadCurrentCulture = appCulture;
            CultureInfo.CurrentCulture = appCulture;
            LocalizationResourceManager.Instance.SetCulture(appCulture);

            Current.UserAppTheme = AppTheme.Dark;

            this.authService = authService;
            this.shell = shell;
            this.workoutService = workoutService;

            MainPage = new ContentPage();
            _ = InitializeApp();
        }

        private async Task InitializeApp()
        {
            var token = await authService.GetTokenAsync();

            if (token == null)
            {
                MainPage = new NavigationPage(IPlatformApplication.Current.Services.GetService<LoginPage>());
            }
            else
            {
                await authService.InitializeAsync();

                // Splash screen'deyken dashboard verisini ön yükle.
                // MainPage açıldığında veri hazır olacak → loading indicator gerekmez.
                await workoutService.PreloadDashboardAsync();

                MainPage = shell;
            }
        }
    }
}