using FitTrackr.MAUI.Localization;
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

            // FitBot'un ve backend'in seçilen kullanıcı dilinde yanıt üretebilmesi için
            // her isteğe güncel dili Accept-Language header'ı olarak ekle. Backend'in
            // SupportedCultures listesiyle (tr-TR/en-US) birebir eşleşmesi için tam kültür
            // adı ("en-US") gönderilir — kısa kod ("en") ASP.NET Core'un varsayılan eşleştirmesinde
            // "en-US" ile aynı kabul edilmez ve sessizce varsayılan dile (tr-TR) düşülür.
            var languageTag = LocalizationResourceManager.Instance.CurrentCulture.Name;
            request.Headers.AcceptLanguage.Clear();
            request.Headers.AcceptLanguage.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue(languageTag));

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