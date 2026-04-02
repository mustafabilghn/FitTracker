using FitTrackr.MAUI.Pages;

namespace FitTrackr.MAUI.Services
{
    public class AuthHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = await SecureStorage.GetAsync("jwt_token");

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                SecureStorage.Remove("jwt_token");

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    var loginPage = IPlatformApplication.Current.Services.GetService<LoginPage>();

                    if (loginPage != null)
                        Application.Current.MainPage = new NavigationPage(loginPage);
                });
            }

            return response;
        }
    }
}