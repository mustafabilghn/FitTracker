using FitTrackr.MAUI.Pages;
using FitTrackr.MAUI.Services;
using System.Globalization;

namespace FitTrackr.MAUI
{
    public partial class App : Application
    {
        private readonly AppShell shell;
        private readonly AuthService authService;

        public App(AppShell shell, AuthService authService)
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

            MainPage = new ContentPage();
            _ = InitializeApp();
        }

        protected override async void OnStart()
        {
            base.OnStart();
            await InitializeApp();
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

                // 🔥 Azure Free Plan cold-start düzeltmesi:
                // MainPage verilerini çekmeden önce API'yi arka planda uyandır.
                // Sonucu beklemiyor, yalnızca HTTP bağlantısını başlatıyoruz.
                // Böylece MainPage.LoadMainPageDataAsync çalıştığında API zaten
                // uyanmakta olacak → bekleme süresi belirgin şekilde kısalır.
                _ = authService.GetProfileAsync();

                MainPage = shell;
            }
        }
    }
}