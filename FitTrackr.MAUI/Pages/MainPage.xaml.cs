using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;

namespace FitTrackr.MAUI
{
    public partial class MainPage : ContentPage
    {
        private readonly WorkoutService _workoutService;
        private readonly AuthService authService;
        private List<WorkoutSummaryDto> workout = new();

        public MainPage(WorkoutService workoutService, AuthService authService)
        {
            InitializeComponent();
            _workoutService = workoutService;

            WeakReferenceMessenger.Default.Register<WorkoutAddedMessage>(this, (r, m) =>
            {
                workout.Add(m.Value);
                TotalWorkoutsLabel.Text = workout.Count.ToString();
                TotalMinutesLabel.Text = $"{workout.Sum(w => w.DurationMinutes)} dk";
                WorkoutsList.ItemsSource = workout.TakeLast(1).ToList();
            });

            WeakReferenceMessenger.Default.Register<WorkoutDeletedMessage>(this, async (r, m) =>
            {
                var workouts = await _workoutService.GetWorkoutsAsync();
                workout = workouts;
                WorkoutsList.ItemsSource = workout.TakeLast(1).ToList();
            });
            this.authService = authService;
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
                workout = workouts;

                TotalWorkoutsLabel.Text = workouts.Count.ToString();
                TotalMinutesLabel.Text = $"{workouts.Sum(w => w.DurationMinutes)} dk";

                var token = await authService.GetTokenAsync();
                string displayName = "Kullanıcı";

                if(token != null)
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(token);
                    var fullName = jwt.Claims
                        .FirstOrDefault(c =>
                        c.Type == "unique_name" ||
                        c.Type == "email" ||
                        c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")
                        ?.Value;

                    displayName = fullName?.Split(' ')[0] ?? "Kullanıcı";
                }

                var hour = DateTime.Now.Hour;
                var greeting = hour < 12 ? "Günaydın" : hour < 18 ? "İyi günler" : "İyi akşamlar";
                GreetingLabel.Text = $"{greeting}, {displayName}";

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
