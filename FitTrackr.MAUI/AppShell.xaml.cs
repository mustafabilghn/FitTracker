using FitTrackr.MAUI.Pages;
using FitTrackr.MAUI.Services;

namespace FitTrackr.MAUI
{
    public partial class AppShell : Shell
    {
        private readonly AuthService authService;

        public AppShell(AuthService authService)
        {
            InitializeComponent();
            this.authService = authService;

            Routing.RegisterRoute("LoginPage", typeof(LoginPage));
            Routing.RegisterRoute("RegisterPage", typeof(RegisterPage));
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CheckAuthAsync();
        }

        private async Task CheckAuthAsync()
        {
            await authService.InitializeAsync();
            var token = await authService.GetTokenAsync();

            if(token == null)
            {
                await GoToAsync("LoginPage");
            }
        }
    }
}
