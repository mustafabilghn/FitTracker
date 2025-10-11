using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;

namespace FitTrackr.MAUI
{
    public partial class MainPage : ContentPage
    {
        private readonly WorkoutService _workoutService;
        private List<WorkoutSummaryDto> workout = new();

        public MainPage(WorkoutService workoutService)
        {
            InitializeComponent();
            _workoutService = workoutService;

            WeakReferenceMessenger.Default.Register<WorkoutAddedMessage>(this, (r, m) =>
            {
                workout.Add(m.Value);
                WorkoutsList.ItemsSource = workout.TakeLast(1).ToList();
            });

            WeakReferenceMessenger.Default.Register<WorkoutDeletedMessage>(this, async (r, m) =>
            {
                var workouts = await _workoutService.GetWorkoutsAsync();
                workout = workouts;
                WorkoutsList.ItemsSource = workout.TakeLast(1).ToList();
            });
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

                WorkoutsList.ItemsSource = workouts.TakeLast(1).ToList();
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
