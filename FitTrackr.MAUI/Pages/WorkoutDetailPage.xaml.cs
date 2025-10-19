using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class WorkoutDetailPage : ContentPage
{
    private readonly WorkoutDetailViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;

    public WorkoutDetailPage(WorkoutDetailViewModel viewModel,IServiceProvider serviceProvider)
    {
        InitializeComponent();

        BindingContext = _viewModel = viewModel;
        _serviceProvider = serviceProvider;
    }

    private async void OnAddExerciseClicked(object sender, EventArgs e)
    {
        var addExercisePage = ActivatorUtilities.CreateInstance<AddExercisePage>(_serviceProvider, _viewModel.Workout.Id);

        await Navigation.PushAsync(addExercisePage);
    }
}