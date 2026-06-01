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

            var trCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.DefaultThreadCurrentCulture = trCulture;
            CultureInfo.DefaultThreadCurrentUICulture = trCulture;
            CultureInfo.CurrentCulture = trCulture;
            CultureInfo.CurrentUICulture = trCulture;

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