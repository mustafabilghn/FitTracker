using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class ExerciseSelectionPage : ContentPage
{
    private readonly ExerciseSelectionViewModel _viewModel;
    private readonly Guid _workoutId;
    private readonly DateTime _workoutDate;
    private readonly string _workoutName;
    private bool _isLoaded;

    public ExerciseSelectionPage(
        ExerciseSelectionViewModel viewModel,
        Guid workoutId,
        DateTime workoutDate,
        string workoutName)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _workoutId = workoutId;
        _workoutDate = workoutDate.Date;
        _workoutName = string.IsNullOrWhiteSpace(workoutName) ? "Antrenman" : workoutName;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        await _viewModel.InitializeAsync();
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
