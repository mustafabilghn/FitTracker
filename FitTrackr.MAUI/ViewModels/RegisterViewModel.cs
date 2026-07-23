using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitTrackr.MAUI.Localization;
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
                await Application.Current.MainPage.DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["Register_UsernameEmptyError"], LocalizationResourceManager.Instance["Common_OK"]);
                return;
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                await Application.Current.MainPage.DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["Register_EmailEmptyError"], LocalizationResourceManager.Instance["Common_OK"]);
                return;
            }

            if (!IsValidEmail(email))
            {
                await Application.Current.MainPage.DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["Login_InvalidEmailError"], LocalizationResourceManager.Instance["Common_OK"]);
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                await Application.Current.MainPage.DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["Register_PasswordEmptyError"], LocalizationResourceManager.Instance["Common_OK"]);
                return;
            }

            if (password.Length < 6)
            {
                await Application.Current.MainPage.DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["ForgotPassword_PasswordLengthError"], LocalizationResourceManager.Instance["Common_OK"]);
                return;
            }

            if (password != confirmPassword)
            {
                await Application.Current.MainPage.DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["ForgotPassword_PasswordsMismatchError"], LocalizationResourceManager.Instance["Common_OK"]);
                return;
            }

            IsLoading = true;

            try
            {
                var success = await authService.RegisterAsync(username, email, password);

                if (success)
                {
                    await Application.Current.MainPage.DisplayAlert(LocalizationResourceManager.Instance["Register_SuccessTitle"], LocalizationResourceManager.Instance["Register_SuccessMessage"], LocalizationResourceManager.Instance["Common_OK"]);
                    await Application.Current.MainPage.Navigation.PopAsync();
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["Register_FailedError"], LocalizationResourceManager.Instance["Common_OK"]);
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
