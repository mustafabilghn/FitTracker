using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages;

public partial class WorkoutDetailPage : ContentPage
{
    private readonly WorkoutDetailViewModel _viewModel;

    public WorkoutDetailPage(WorkoutDetailViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = _viewModel = viewModel;
    }
}