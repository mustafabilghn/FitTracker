using FitTrackr.MAUI.Services;
using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class WorkoutListPage : ContentPage
{
    private readonly WorkoutListViewModel _viewModel;

    public WorkoutListPage(WorkoutListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            await _viewModel.LoadWorkoutsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Hata", $"Antrenmanlar yüklenirken bir hata meydana geldi: {ex.Message}", "Tamam");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }
}