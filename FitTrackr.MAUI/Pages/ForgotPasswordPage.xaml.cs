using FitTrackr.MAUI.Localization;
using FitTrackr.MAUI.Services;

namespace FitTrackr.MAUI.Pages;

public partial class ForgotPasswordPage : ContentPage
{
    private readonly AuthService _authService;
    private string _email = string.Empty;

    public ForgotPasswordPage(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    private void OnBackClicked(object sender, TappedEventArgs e)
    {
        Navigation.PopAsync();
    }

    // ── ADIM 1: Kod gönder ────────────────────────────────────────────────────
    private async void OnSendCodeClicked(object sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email))
        {
            await DisplayAlert(LocalizationResourceManager.Instance["Common_Warning"], LocalizationResourceManager.Instance["ForgotPassword_EmailRequiredError"], LocalizationResourceManager.Instance["Common_OK"]);
            return;
        }

        SendCodeButton.IsEnabled = false;
        Step1Indicator.IsVisible = true;
        Step1Indicator.IsRunning = true;

        try
        {
            var success = await _authService.ForgotPasswordAsync(email);

            if (!success)
            {
                await DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], LocalizationResourceManager.Instance["ForgotPassword_RequestFailedError"], LocalizationResourceManager.Instance["Common_OK"]);
                return;
            }

            _email = email;
            Step2HintLabel.Text = string.Format(LocalizationResourceManager.Instance["ForgotPassword_CodeSentFormat"], email);
            StepOneLayout.IsVisible = false;
            StepTwoLayout.IsVisible = true;
        }
        finally
        {
            SendCodeButton.IsEnabled = true;
            Step1Indicator.IsVisible = false;
            Step1Indicator.IsRunning = false;
        }
    }

    // ── ADIM 2: Şifreyi sıfırla ───────────────────────────────────────────────
    private async void OnResetPasswordClicked(object sender, EventArgs e)
    {
        var code        = CodeEntry.Text?.Trim() ?? string.Empty;
        var newPassword = NewPasswordEntry.Text  ?? string.Empty;
        var confirm     = ConfirmPasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            await DisplayAlert(LocalizationResourceManager.Instance["Common_Warning"], LocalizationResourceManager.Instance["ForgotPassword_CodeRequiredError"], LocalizationResourceManager.Instance["Common_OK"]);
            return;
        }

        if (newPassword.Length < 6)
        {
            await DisplayAlert(LocalizationResourceManager.Instance["Common_Warning"], LocalizationResourceManager.Instance["ForgotPassword_PasswordLengthError"], LocalizationResourceManager.Instance["Common_OK"]);
            return;
        }

        if (newPassword != confirm)
        {
            await DisplayAlert(LocalizationResourceManager.Instance["Common_Warning"], LocalizationResourceManager.Instance["ForgotPassword_PasswordsMismatchError"], LocalizationResourceManager.Instance["Common_OK"]);
            return;
        }

        ResetButton.IsEnabled = false;
        Step2Indicator.IsVisible = true;
        Step2Indicator.IsRunning = true;

        try
        {
            var (success, error) = await _authService.ResetPasswordAsync(_email, code, newPassword);

            if (success)
            {
                await DisplayAlert(LocalizationResourceManager.Instance["ForgotPassword_SuccessTitle"], LocalizationResourceManager.Instance["ForgotPassword_SuccessMessage"], LocalizationResourceManager.Instance["Common_OK"]);
                await Navigation.PopAsync();
            }
            else
            {
                var message = string.IsNullOrWhiteSpace(error)
                    ? LocalizationResourceManager.Instance["ForgotPassword_CodeInvalidError"]
                    : error;
                await DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], message, LocalizationResourceManager.Instance["Common_OK"]);
            }
        }
        finally
        {
            ResetButton.IsEnabled = true;
            Step2Indicator.IsVisible = false;
            Step2Indicator.IsRunning = false;
        }
    }

    // Kodu tekrar gönder — adım 1'e dön
    private void OnResendCodeClicked(object sender, TappedEventArgs e)
    {
        EmailEntry.Text = _email;
        StepTwoLayout.IsVisible = false;
        StepOneLayout.IsVisible = true;
        CodeEntry.Text = string.Empty;
        NewPasswordEntry.Text = string.Empty;
        ConfirmPasswordEntry.Text = string.Empty;
    }
}
