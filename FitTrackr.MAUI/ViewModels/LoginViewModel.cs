using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitTrackr.MAUI.Localization;
using FitTrackr.MAUI.Services;
using System.Text.RegularExpressions;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly AuthService authService;
        private readonly IGoogleAuthService googleAuthService;

        [ObservableProperty]
        private string email = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotLoading))]
        private bool isLoading = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotLoading))]
        private bool isGoogleLoading = false;

        /// <summary>Her iki yükleme durumu da false olduğunda butonlar aktif olur.</summary>
        public bool IsNotLoading => !IsLoading && !IsGoogleLoading;

        public LoginViewModel(AuthService authService, IGoogleAuthService googleAuthService)
        {
            this.authService = authService;
            this.googleAuthService = googleAuthService;
        }

        [RelayCommand]
        public async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                await Application.Current.MainPage.DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["Login_EmptyFieldsError"], LocalizationResourceManager.Instance["Common_OK"]);
                return;
            }

            if (!IsValidEmail(email))
            {
                await Application.Current.MainPage.DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["Login_InvalidEmailError"], LocalizationResourceManager.Instance["Common_OK"]);
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
                    await Application.Current.MainPage.DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["Login_InvalidCredentialsError"], LocalizationResourceManager.Instance["Common_OK"]);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task LoginWithGoogleAsync()
        {
            IsGoogleLoading = true;

            try
            {
                // 1. WebAuthenticator ile Google akışını başlat — authorization code al
                var oauthResult = await googleAuthService.GetAuthorizationCodeAsync();

                // Kullanıcı iptal ettiyse ya da hata oluştuysa — sessizce çık
                if (oauthResult == null)
                    return;

                // 2. Backend'e code + code_verifier gönder — JWT al
                var success = await authService.GoogleLoginAsync(
                    oauthResult.Code,
                    oauthResult.CodeVerifier,
                    oauthResult.RedirectUri);

                if (success)
                {
                    Application.Current.MainPage = IPlatformApplication.Current.Services.GetService<AppShell>();
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert(
                        LocalizationResourceManager.Instance["Common_Error"],
                        LocalizationResourceManager.Instance["Login_GoogleSignInError"],
                        LocalizationResourceManager.Instance["Common_OK"]);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoginViewModel] Google login error: {ex.Message}");
                await Application.Current.MainPage.DisplayAlert(
                    LocalizationResourceManager.Instance["Common_Error"],
                    LocalizationResourceManager.Instance["Login_GenericError"],
                    LocalizationResourceManager.Instance["Common_OK"]);
            }
            finally
            {
                IsGoogleLoading = false;
            }
        }

        private static bool IsValidEmail(string value) =>
            Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);
    }
}
