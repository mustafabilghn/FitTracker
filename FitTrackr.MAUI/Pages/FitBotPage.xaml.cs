using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class FitBotPage : ContentPage
{
    public FitBotPage(FitBotViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
