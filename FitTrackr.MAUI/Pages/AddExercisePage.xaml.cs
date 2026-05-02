using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Validations;
using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class AddExercisePage : ContentPage
{
    private readonly Guid _workoutId;
    private readonly DateTime _workoutDate;
    private readonly AddExerciseViewModel _viewModel;

    public AddExercisePage(AddExerciseViewModel viewModel, Guid workoutId, DateTime workoutDate)
    {
        InitializeComponent();

        BindingContext = _viewModel = viewModel;
        _workoutId = workoutId;
        _workoutDate = workoutDate.Date;
    }

    protected override async void OnAppearing()
    {
        await _viewModel.LoadIntensitiesAsync();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var exerciseName = ExerciseNameEntry.Text;
        var notes = NotesEntry.Text;
        var intensity = IntensityPicker.SelectedItem as IntensityDto;

        var error = ExerciseValidator.Validate(exerciseName, intensity?.Id ?? Guid.Empty);

        if (!string.IsNullOrEmpty(error))
        {
            await DisplayAlert("Hata", error, "Tamam");
            return;
        }

        var exercise = new ExerciseRequestDto
        {
            ExerciseName = exerciseName,
            Notes = notes,
            IntensityId = intensity?.Id ?? Guid.Empty,
            WorkoutId = _workoutId,
        };

        await _viewModel.AddExerciseAsync(exercise, _workoutDate);
        await DisplayAlert("Başarılı", "Egzersiz başarıyla eklendi.", "Tamam");
        await Navigation.PopAsync();
    }
}
