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
    public partial class RegisterViewModel : ObservableObject
    {
        private readonly AuthService authService;

        [ObservableProperty]
        private string username = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        [ObservableProperty]
        private string confirmPassword = string.Empty;

        public RegisterViewModel(AuthService authService)
        {
            this.authService = authService;
        }

        [RelayCommand]
        public async Task RegisterAsync()
        {
            if(string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Kullanıcı adı ve şifre boş olamaz.", "Tamam");
                return;
            }

            if(password != confirmPassword)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Şifreler eşleşmiyor.", "Tamam");
                return;
            }

            var success = await authService.RegisterAsync(username, password);

            if (success)
            {
                await Application.Current.MainPage.DisplayAlert("Başarılı", "Hesabın oluşturuldu, giriş yapabilirsin.", "Tamam");
                await Application.Current.MainPage.Navigation.PopAsync();
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Kayıt sırasında bir hata oluştu. Lütfen tekrar deneyin.", "Tamam");
            }
        }
    }
}
