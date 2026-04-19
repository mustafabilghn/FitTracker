using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class ExerciseSetEntryPage : ContentPage
{
    public ExerciseSetEntryPage(
        ExerciseSetEntryViewModel viewModel,
        ExerciseCatalogItemDto selectedExercise,
        Guid? workoutId,
        DateTime workoutDate,
        string workoutName)
    {
        InitializeComponent();
        BindingContext = viewModel;

        viewModel.Initialize(selectedExercise, workoutId, workoutDate, workoutName);
    }

    public ExerciseSetEntryPage(
        ExerciseSetEntryViewModel viewModel,
        Guid exerciseId,
        string exerciseName)
    {
        InitializeComponent();
        BindingContext = viewModel;

        viewModel.InitializeForExistingExercise(exerciseId, exerciseName);
    }
}
