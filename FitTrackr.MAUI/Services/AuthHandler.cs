using FitTrackr.MAUI.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitTrackr.MAUI.Services
{
    public class AuthHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(httpRequestMessage, cancellationToken);

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
