using FitTrackr.MAUI.ViewModels;

namespace FitTrackr.MAUI
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; }

        public App()
        {
            InitializeComponent();

            ServiceProvider = MauiProgram.CreateMauiApp().Services;

            MainPage = ServiceProvider.GetService<AppShell>();
        }
    }
}