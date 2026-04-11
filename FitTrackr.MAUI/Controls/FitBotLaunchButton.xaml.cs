using FitTrackr.MAUI.Pages;

namespace FitTrackr.MAUI.Controls;

public partial class FitBotLaunchButton : ContentView
{
    public FitBotLaunchButton()
    {
        InitializeComponent();
    }

    private async void OnTapped(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(FitBotPage));
    }
}
