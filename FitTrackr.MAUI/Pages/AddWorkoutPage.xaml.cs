using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Validations;
using FitTrackr.MAUI.ViewModels;
using System.Globalization;

namespace FitTrackr.MAUI.Pages;

public partial class AddWorkoutPage : ContentPage
{
    private readonly WorkoutListViewModel _viewModel;

    public AddWorkoutPage()
    {
        InitializeComponent();

        _viewModel = App.ServiceProvider.GetService<WorkoutListViewModel>()!;

        BindingContext = _viewModel;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            string workoutName = WorkoutNameEntry.Text?.Trim() ?? string.Empty;
            string dayInput = DayEntry.Text?.Trim() ?? string.Empty;
            string duration = DurationEntry.Text?.Trim() ?? string.Empty;
            var location = LocationPicker.SelectedItem;

            var validationError = WorkoutRequestValidator.Validate(workoutName, dayInput, duration, location);

            if (validationError != null)
            {
                await DisplayAlert("Hata", validationError, "Tamam");
                return;
            }

            var culture = CultureInfo.GetCultureInfo("tr-TR");

            DayOfWeek selectedDay;

            try
            {
                selectedDay = Enum.GetValues(typeof(DayOfWeek))
                    .Cast<DayOfWeek>().First(d => string.Equals(culture.DateTimeFormat.GetDayName(d), dayInput, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                await DisplayAlert("Hata", "Geçersiz gün girdiniz", "Tamam");
                return;
            }

            var request = new WorkoutRequestDto
            {
                WorkoutName = workoutName,
                DurationMinutes = double.Parse(duration),
                WorkoutDate = selectedDay,
                LocationId = (location as LocationDto)?.Id ?? Guid.Empty
            };

            await _viewModel.AddWorkoutAsync(request);

            await DisplayAlert("Baþarýlý", "Antrenman eklendi", "Tamam");
            await Navigation.PopAsync();
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

}