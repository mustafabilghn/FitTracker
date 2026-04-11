using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;

namespace FitTrackr.MAUI
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            Window?.SetStatusBarColor(Android.Graphics.Color.ParseColor("#121212"));

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                Window?.InsetsController?.SetSystemBarsAppearance(0, (int)WindowInsetsControllerAppearance.LightStatusBars);
            }
        }
    }
}

