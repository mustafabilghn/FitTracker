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

        public async void OnViewWorkoutsClicked(object sender, EventArgs e)
        {
            try
            {
                var workouts = await _workoutService.GetWorkoutsAsync();
                WorkoutsList.ItemsSource = workouts;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load workouts: {ex.Message}", "OK");
            }
        }

        private void OnAddWorkoutClicked(object sender, EventArgs e)
        {
            // Navigate to Add Workout Page (to be implemented)
        }
    }
}
