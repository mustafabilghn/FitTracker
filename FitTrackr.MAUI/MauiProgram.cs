using FitTrackr.MAUI.Configuration;
using FitTrackr.MAUI.Pages;
using FitTrackr.MAUI.Services;
using FitTrackr.MAUI.ViewModels;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FitTrackr.MAUI
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton(sp =>
            {
                var handler = new AuthHandler
                {
                    InnerHandler = new HttpClientHandler()
                };

                var client = new HttpClient(handler)
                {
                    BaseAddress = ApiSettings.ApiBaseUri,
                    Timeout = TimeSpan.FromSeconds(30)
                };

                return client;
            });

            builder.Services.AddSingleton(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            });

            builder.Services.AddSingleton<WorkoutService>();
            builder.Services.AddSingleton<ExerciseService>();
            builder.Services.AddSingleton<ExerciseSetService>();
            builder.Services.AddSingleton<AuthService>();

            builder.Services.AddTransient<WorkoutListPage>();
            builder.Services.AddTransient<WorkoutListViewModel>();
            builder.Services.AddTransient<AddWorkoutPage>();
            builder.Services.AddTransient<AddWorkoutViewModel>();
            builder.Services.AddTransient<WorkoutDetailPage>();
            builder.Services.AddTransient<WorkoutDetailViewModel>();
            builder.Services.AddTransient<AddExercisePage>();
            builder.Services.AddTransient<AddExerciseViewModel>();
            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<LoginViewModel>();
            builder.Services.AddTransient<RegisterPage>();
            builder.Services.AddTransient<RegisterViewModel>();
            builder.Services.AddTransient<AuthHandler>();
            builder.Services.AddTransient<ProfilePage>();
            builder.Services.AddTransient<ProfileViewModel>();
            builder.Services.AddTransient<AppShell>();
            builder.Services.AddTransient<MainPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
