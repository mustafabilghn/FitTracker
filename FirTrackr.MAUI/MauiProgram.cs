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

            builder.Services.AddSingleton(new HttpClient
            {
                BaseAddress = new Uri("https://fittracker-stqv.onrender.com/")
            });

            builder.Services.AddSingleton(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            });

            builder.Services.AddSingleton<WorkoutService>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddSingleton<WorkoutListViewModel>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
