using FitTrackr.MAUI.Localization;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Validations;
using FitTrackr.MAUI.ViewModels;


namespace FitTrackr.MAUI.Pages;

public partial class AddWorkoutPage : ContentPage
{
    private readonly AddWorkoutViewModel _viewModel;

    public AddWorkoutPage(AddWorkoutViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        WorkoutDatePicker.Date = DateTime.Today;
        await _viewModel.LoadLocationsAsync();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var workoutName = WorkoutNameEntry.Text;
        var workoutDate = WorkoutDatePicker.Date;
        var duration = DurationMinutes.Text;
        var location = LocationPicker.SelectedItem as LocationDto;

        var workout = new WorkoutRequestDto
        {
            WorkoutName = workoutName,
            WorkoutDate = workoutDate.Date,
            DurationMinutes = double.TryParse(duration,out var result) ? result : 0,
            LocationId = location?.Id ?? Guid.Empty,
        };

        var error = WorkoutValidator.Validate(workoutName, workout.DurationMinutes, workout.LocationId, workoutDate);

        if (!string.IsNullOrEmpty(error))
        {
            await DisplayAlert(LocalizationResourceManager.Instance["Common_Error"], error, LocalizationResourceManager.Instance["Common_OK"]);
            return;
        }

        await _viewModel.AddWorkoutAsync(workout);
        await DisplayAlert(LocalizationResourceManager.Instance["AddWorkout_SuccessTitle"], LocalizationResourceManager.Instance["AddWorkout_SuccessMessage"], LocalizationResourceManager.Instance["Common_OK"]);
        await Navigation.PopAsync();
    }
}