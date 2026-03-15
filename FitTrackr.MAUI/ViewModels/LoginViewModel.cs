using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitTrackr.MAUI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly AuthService authService;

        [ObservableProperty]
        private string username = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        public LoginViewModel(AuthService authService)
        {
            this.authService = authService;
        }

        [RelayCommand]
        public async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Kullanıcı adı ve şifre boş olamaz.", "Tamam");
                return;
            }

            var success = await authService.LoginAsync(username, password);

            if (success)
            {
                Application.Current.MainPage = IPlatformApplication.Current.Services.GetService<AppShell>();
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Kullanıcı adı veya şifre hatalı.", "Tamam");
            }
        }
    }
}
