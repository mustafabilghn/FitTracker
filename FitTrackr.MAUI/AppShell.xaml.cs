using FitTrackr.MAUI.Pages;
using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI
{
    public partial class AppShell : Shell
    {
        private readonly WorkoutListViewModel viewModel;

        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(AddWorkoutPage), typeof(AddWorkoutPage));

            viewModel = App.ServiceProvider.GetService<WorkoutListViewModel>();

            Task.Run(() => viewModel.LoadWorkoutsAsync());
        }
    }
}
