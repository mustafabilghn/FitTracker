using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class ExerciseSelectionPage : ContentPage
{
    private readonly Guid _workoutId;
    private readonly DateTime _workoutDate;
    private readonly string _workoutName;

    public ExerciseSelectionPage(
        ExerciseSelectionViewModel viewModel,
        Guid workoutId,
        DateTime workoutDate,
        string workoutName)
    {
        InitializeComponent();
        BindingContext = viewModel;

        _workoutId = workoutId;
        _workoutDate = workoutDate.Date;
        _workoutName = string.IsNullOrWhiteSpace(workoutName) ? "Antrenman" : workoutName;
    }

    private async void OnExerciseTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not ExerciseCatalogItemDto selectedExercise)
        {
            return;
        }

        var services = Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("Page services are not available.");

        var setEntryPage = ActivatorUtilities.CreateInstance<ExerciseSetEntryPage>(
            services,
            selectedExercise,
            _workoutId,
            _workoutDate,
            _workoutName);

        await Navigation.PushAsync(setEntryPage);
    }
}