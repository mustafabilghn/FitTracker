using FitTrackr.MAUI.Services;

namespace FitTrackr.MAUI
{
    public partial class MainPage : ContentPage
    {
        private readonly WorkoutService _workoutService;

        public MainPage(WorkoutService workoutService)
        {
            InitializeComponent();
            _workoutService = workoutService;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (WorkoutsList.ItemsSource != null)
                return;

            try
            {
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;

                var workouts = await _workoutService.GetWorkoutsAsync();

                WorkoutsList.ItemsSource = workouts;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Hata", $"Veriler alınırken bir hata oluştu: {ex.Message}", "Tamam");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }

        }
    }
}
