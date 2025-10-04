using FitTrackr.MAUI.Pages;

namespace FitTrackr.MAUI
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(MainPage), typeof(MainPage));
            Routing.RegisterRoute(nameof(WorkoutListPage), typeof(WorkoutListPage));
        }
    }
}
