using FitTrackr.MAUI.Services;
using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class WorkoutListPage : ContentPage
{
    private readonly WorkoutListViewModel _viewModel;
    private readonly IServiceProvider serviceProvider;

    public WorkoutListPage(WorkoutListViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        this.serviceProvider = serviceProvider;
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
            await DisplayAlert("Hata", $"Antrenmanlar y�klenirken bir hata meydana geldi: {ex.Message}", "Tamam");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }

    private async void OnAddWorkoutClicked(object sender, EventArgs e)
    {
       var addWorkoutPage = serviceProvider.GetService<AddWorkoutPage>();

        await Navigation.PushAsync(addWorkoutPage);
    }
}