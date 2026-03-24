using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitTrackr.MAUI.Pages;
using FitTrackr.MAUI.Services;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class ProfileViewModel : ObservableObject
    {
        private readonly AuthService authService;

        [ObservableProperty]
        public string username = string.Empty;

        public string UsernameInitial => string.IsNullOrEmpty(Username)
            ? "?"
            : Username[0].ToString().ToUpper();

        public ProfileViewModel(AuthService authService)
        {
            this.authService = authService;
        }

        public async Task LoadProfileAsync()
        {
            var token = await authService.GetTokenAsync();

            if (token != null)
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);

                Username = jwt.Claims.FirstOrDefault(c => c.Type == "unique_name" || c.Type == "email" ||
                c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")
                    ?.Value ?? "Kullanıcı";

                OnPropertyChanged(nameof(UsernameInitial));
            }
        }

        [RelayCommand]
        public async Task LogoutAsync()
        {
            authService.Logout();

            Application.Current.MainPage = new NavigationPage(
                IPlatformApplication.Current.Services.GetService<LoginPage>());
        }

        [RelayCommand]
        public async Task ConfirmDeleteAccountAsync()
        {
            bool isConfirmed = await Application.Current.MainPage.DisplayAlert(
                "Hesabı Sil",
                "Bu işlemi gerçekleştirmek istediğinizden emin misiniz? Bu işlem geri alınamaz.",
                "Evet",
                "Vazgeç");

            if (isConfirmed)
            {
                var success = await authService.DeleteAccountAsync();

                if (success)
                {
                    await Application.Current.MainPage.DisplayAlert("Başarılı", "Hesabınız ve tüm verileriniz kalıcı olarak silindi.", "Tamam");

                    authService.Logout();
                    Application.Current.MainPage = new NavigationPage(
                        IPlatformApplication.Current.Services.GetService<LoginPage>());
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", "Hesap silme işlemi sırasında bir hata oluştu. Lütfen bağlantınızı kontrol edip tekrar deneyin.", "Tamam");
                }
            }
        }
    }
}
