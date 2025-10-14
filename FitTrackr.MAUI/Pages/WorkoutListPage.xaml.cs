using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
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

        WeakReferenceMessenger.Default.Register<WorkoutSelectedMessage>(this, async (r, m) =>
        {
            try
            {
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;

                var workoutDetailPage = serviceProvider.GetService<WorkoutDetailPage>();
                if (workoutDetailPage == null)
                    return;

                if (workoutDetailPage.BindingContext is WorkoutDetailViewModel vm)
                    await vm.LoadWorkoutDetailsAsync(m.Value);

                await Navigation.PushAsync(workoutDetailPage);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Hata", $"Sayfa yüklenirken bir hata meydana geldi: {ex.Message}", "Tamam");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        });
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

    private async void OnAddWorkoutClicked(object sender, EventArgs e)
    {
        var addWorkoutPage = serviceProvider.GetService<AddWorkoutPage>();

        await Navigation.PushAsync(addWorkoutPage);
    }
}