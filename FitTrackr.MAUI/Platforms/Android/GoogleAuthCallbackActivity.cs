using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Authentication;

namespace FitTrackr.MAUI.Platforms.Android
{
    /// <summary>
    /// Backend'in gonderdigi "fittrackr://auth?code=..." deep link'ini yakalar.
    ///
    /// Akis:
    ///   Google → backend HTTPS callback → backend 302 → fittrackr://auth?code=xxx
    ///   → Android bu Activity'e teslim eder → WebAuthenticatorCallbackActivity tamamlar
    ///   → WebAuthenticator.AuthenticateAsync sonuclenir
    ///
    /// CallbackUrl (GoogleAuthService.cs): fittrackr://auth
    ///   scheme = fittrackr
    ///   host   = auth
    /// </summary>
    [Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = "fittrackr",
        DataHost = "auth")]
    public class GoogleAuthCallbackActivity : WebAuthenticatorCallbackActivity
    {
    }
}
