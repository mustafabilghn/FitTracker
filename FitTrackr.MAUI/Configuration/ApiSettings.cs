namespace FitTrackr.MAUI.Configuration
{
    /// <summary>
    /// Central API base URL: DEBUG uses local dev (Android emulator vs desktop), RELEASE uses production.
    /// </summary>
    public static class ApiSettings
    {
        private const string ProductionBaseUrl = "https://fittracker-production-2c5c.up.railway.app/";
        private const string AndroidDebugBaseUrl = "http://10.0.2.2:5187/";
        private const string LocalDebugBaseUrl = "http://localhost:5187/";

        public static Uri ApiBaseUri => new(GetBaseUrlString(), UriKind.Absolute);

        private static string GetBaseUrlString()
        {
#if DEBUG
            return DeviceInfo.Platform == DevicePlatform.Android
                ? AndroidDebugBaseUrl
                : LocalDebugBaseUrl;
#else
            return ProductionBaseUrl;
#endif
        }
    }
}
