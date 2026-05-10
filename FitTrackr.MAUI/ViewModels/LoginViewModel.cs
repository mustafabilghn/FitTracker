using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitTrackr.MAUI.Services;
using System.Text.RegularExpressions;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly AuthService authService;

        [ObservableProperty]
        private string email = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotLoading))]
        private bool isLoading = false;

        public bool IsNotLoading => !IsLoading;

        public LoginViewModel(AuthService authService)
        {
            this.authService = authService;
        }

        [RelayCommand]
        public async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "E-posta ve şifre boş olamaz.", "Tamam");
                return;
            }

            if (!IsValidEmail(email))
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Geçerli bir e-posta adresi giriniz.", "Tamam");
                return;
            }

            IsLoading = true;

            try
            {
                var success = await authService.LoginAsync(email, password);

                if (success)
                {
                    Application.Current.MainPage = IPlatformApplication.Current.Services.GetService<AppShell>();
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", "E-posta veya şifre hatalı.", "Tamam");
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static bool IsValidEmail(string value) =>
            Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);
    }
}
