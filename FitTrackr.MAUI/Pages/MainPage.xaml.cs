using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI.Pages
{
    public partial class MainPage : ContentPage
    {
        private readonly WorkoutListViewModel _viewModel;

        public MainPage(WorkoutListViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = _viewModel = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            await _viewModel.LoadWorkoutsAsync();
        }

        public async void OnViewWorkoutsClicked(object sender, EventArgs e)
        {
            await _viewModel.LoadWorkoutsAsync();
        }

        public async void OnAddWorkoutClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(AddWorkoutPage));
        }
    }
}
