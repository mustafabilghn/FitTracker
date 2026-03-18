using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class ProfilePage : ContentPage
{
	private readonly ProfileViewModel viewModel;

    public ProfilePage(ProfileViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadProfileAsync();
    }
}