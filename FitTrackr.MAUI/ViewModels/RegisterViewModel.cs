using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitTrackr.MAUI.Services;
using System.Text.RegularExpressions;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class RegisterViewModel : ObservableObject
    {
        private readonly AuthService authService;

        [ObservableProperty]
        private string username = string.Empty;

        [ObservableProperty]
        private string email = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        [ObservableProperty]
        private string confirmPassword = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotLoading))]
        private bool isLoading = false;

        public bool IsNotLoading => !IsLoading;

        public RegisterViewModel(AuthService authService)
        {
            this.authService = authService;
        }

        [RelayCommand]
        public async Task RegisterAsync()
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Kullanıcı adı boş olamaz.", "Tamam");
                return;
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "E-posta adresi boş olamaz.", "Tamam");
                return;
            }

            if (!IsValidEmail(email))
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Geçerli bir e-posta adresi giriniz.", "Tamam");
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Şifre boş olamaz.", "Tamam");
                return;
            }

            if (password.Length < 6)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Şifre en az 6 karakter olmalıdır.", "Tamam");
                return;
            }

            if (password != confirmPassword)
            {
                await Application.Current.MainPage.DisplayAlert("Hata", "Şifreler eşleşmiyor.", "Tamam");
                return;
            }

            IsLoading = true;

            try
            {
                var success = await authService.RegisterAsync(username, email, password);

                if (success)
                {
                    await Application.Current.MainPage.DisplayAlert("Başarılı", "Hesabın oluşturuldu, giriş yapabilirsin.", "Tamam");
                    await Application.Current.MainPage.Navigation.PopAsync();
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("Hata", "Kayıt sırasında bir hata oluştu. E-posta veya kullanıcı adı zaten kullanılıyor olabilir.", "Tamam");
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
