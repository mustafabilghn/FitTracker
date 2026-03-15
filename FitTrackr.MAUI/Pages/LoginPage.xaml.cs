using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class LoginPage : ContentPage
{
	public LoginPage(LoginViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
    }

	private async void OnRegisterClicked(Object sender,EventArgs e)
	{
        var registerPage = IPlatformApplication.Current.Services.GetService<RegisterPage>();
		await Navigation.PushAsync(registerPage);
    }
}