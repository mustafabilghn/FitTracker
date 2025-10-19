using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Validations;
using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class AddExercisePage : ContentPage
{
    private readonly Guid _workoutId;
    private readonly AddExerciseViewModel _viewModel;

    public AddExercisePage(AddExerciseViewModel viewModel, Guid workoutId)
    {
        InitializeComponent();

        BindingContext = _viewModel = viewModel;
        _workoutId = workoutId;
    }

    protected override async void OnAppearing()
    {
        await _viewModel.LoadIntensitiesAsync();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var exerciseName = ExerciseNameEntry.Text;
        var sets = SetsEntry.Text;
        var reps = RepsEntry.Text;
        var weight = WeightEntry.Text;
        var intensity = IntensityPicker.SelectedItem as IntensityDto;

        var exercise = new ExerciseRequestDto
        {
            ExerciseName = exerciseName,
            Sets = int.TryParse(sets, out var setsResult) ? setsResult : 0,
            Reps = reps,
            WeightInKg = double.TryParse(weight, out var weightResult) ? weightResult : 0,
            IntensityId = intensity?.Id ?? Guid.Empty,
            WorkoutId = _workoutId,
        };

        var error = ExerciseValidator.Validate(exerciseName, exercise.Sets, reps, exercise.WeightInKg, exercise.IntensityId);

        if (!string.IsNullOrEmpty(error))
        {
            await DisplayAlert("Hata", error, "Tamam");
            return;
        }

        await _viewModel.AddExerciseAsync(exercise);
        await DisplayAlert("Baþarýlý", "Egzersiz baþarýyla eklendi.", "Tamam");
        await Navigation.PopAsync();
    }
}