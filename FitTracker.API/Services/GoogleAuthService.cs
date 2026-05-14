using FitTrackr.API.Services.Interfaces;
using Google.Apis.Auth;
using System.Text.Json;

namespace FitTrackr.API.Services
{
    /// <summary>
    /// Google OAuth 2.0 authorization code → id_token exchange + doğrulama.
    ///
    /// Güvenlik modeli:
    ///   - client_secret yalnızca backend'de tutulur (appsettings / Key Vault).
    ///   - PKCE, code interception saldırısını önler.
    ///   - id_token hiçbir zaman MAUI uygulamasına gönderilmez.
    /// </summary>
    public class GoogleAuthService : IGoogleAuthService
    {
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

        private readonly IConfiguration configuration;
        private readonly IHttpClientFactory httpClientFactory;

        public GoogleAuthService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            this.configuration = configuration;
            this.httpClientFactory = httpClientFactory;
        }

        public async Task<GoogleJsonWebSignature.Payload> ExchangeAndValidateCodeAsync(
            string code,
            string codeVerifier,
            string redirectUri)
        {
            var clientId = configuration["Google:ClientId"]
                ?? throw new InvalidOperationException("Google:ClientId yapılandırılmamış.");

            var clientSecret = configuration["Google:ClientSecret"]
                ?? throw new InvalidOperationException("Google:ClientSecret yapılandırılmamış.");

            // 1. Authorization code'u id_token ile değiştir
            var idToken = await ExchangeCodeForIdTokenAsync(
                code, codeVerifier, redirectUri, clientId, clientSecret);

            // 2. Google'ın public key'leriyle id_token'ı doğrula
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            };

            // Geçersiz, süresi dolmuş veya başka uygulamaya ait token'da InvalidJwtException fırlatır.
            return await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
        }

        private async Task<string> ExchangeCodeForIdTokenAsync(
            string code,
            string codeVerifier,
            string redirectUri,
            string clientId,
            string clientSecret)
        {
            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier,
                ["grant_type"] = "authorization_code"
            });

            var response = await httpClient.PostAsync(TokenEndpoint, body);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Google token exchange başarısız ({response.StatusCode}): {responseBody}");

            using var json = JsonDocument.Parse(responseBody);

            if (!json.RootElement.TryGetProperty("id_token", out var idTokenProp))
                throw new InvalidOperationException("Google yanıtında id_token bulunamadı.");

            return idTokenProp.GetString()
                ?? throw new InvalidOperationException("id_token değeri null.");
        }
    }
}
