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

            // Kayıtlı dil tercihini oku (yoksa Türkçe varsayılan). ProfilePage'deki dil
            // seçici bu tercihi Preferences.Set("app_language", "tr"|"en") ile günceller.
            var savedLanguage = Preferences.Get("app_language", "tr");
            var appCulture = CultureInfo.GetCultureInfo(savedLanguage == "en" ? "en-US" : "tr-TR");
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