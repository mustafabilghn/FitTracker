using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class RegisterPage : ContentPage
{
	public RegisterPage(RegisterViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
    }

	private async void OnLoginClicked(Object sender, EventArgs e)
	{
		await Navigation.PopAsync();
    }
}