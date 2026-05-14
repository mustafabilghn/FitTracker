using System.Security.Cryptography;
using System.Text;

namespace FitTrackr.MAUI.Services
{
    /// <summary>
    /// MAUI WebAuthenticator + Google OAuth 2.0 PKCE akışı.
    ///
    /// Kullanılan OAuth client tipi: Web application (Google Cloud Console'da)
    ///   — Android client değil; Web client custom URI scheme'e izin verir.
    ///
    /// Token exchange backend'de yapılır:
    ///   MAUI  →  (code + code_verifier + redirect_uri)  →  Backend
    ///   Backend  →  Google token endpoint  →  id_token  →  doğrula  →  JWT
    /// </summary>
    public class GoogleAuthService : IGoogleAuthService
    {
        // ─────────────────────────────────────────────────────────────────────────
        // Google Cloud Console → APIs & Services → Credentials →
        //   + Create Credentials → OAuth 2.0 Client ID → Web application
        //
        //   Authorized redirect URIs →  com.companyname.fittrackr.maui:/oauth2redirect  ekle
        //
        // Oluşturulan Client ID'yi (ANDROID DEĞİL WEB CLIENT ID) aşağıya girin:
        private const string WebClientId = "1078466259667-uamslofa1i2tglttg73seonua76s06e0.apps.googleusercontent.com";
        // ─────────────────────────────────────────────────────────────────────────

        // Google Cloud Console Web OAuth client'inda kayitli HTTPS redirect URI.
        // Google bu adrese code'u gonderir; backend de fittrackr:// deep link ile MAUI'ye iletir.
        // Google Cloud Console > Credentials > Web client > Authorized redirect URIs:
        //   https://fittracker-api-achkb5c8csdncph2.germanywestcentral-01.azurewebsites.net/api/auth/google-callback
        private const string RedirectUri =
            "https://fittracker-api-achkb5c8csdncph2.germanywestcentral-01.azurewebsites.net/api/auth/google-callback";

        // Backend'in gonderdigi deep link — GoogleAuthCallbackActivity bu URI'yi yakalar.
        //   scheme = fittrackr
        //   host   = auth
        private const string CallbackUrl = "fittrackr://auth";

        private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string Scope = "openid email profile";

        public async Task<GoogleOAuthResult?> GetAuthorizationCodeAsync()
        {
            try
            {
                var codeVerifier = GenerateCodeVerifier();
                var codeChallenge = GenerateCodeChallenge(codeVerifier);
                var state = GenerateRandomBase64Url(16);

                var authUrl = BuildAuthUrl(codeChallenge, state);

                var result = await WebAuthenticator.Default.AuthenticateAsync(
                    new WebAuthenticatorOptions
                    {
                        Url = new Uri(authUrl),
                        // WebAuthenticator bu URI geldiginde AuthenticateAsync'i tamamlar
                        CallbackUrl = new Uri(CallbackUrl),
                        PrefersEphemeralWebBrowserSession = false
                    });

                // Google authorization code'u "code" parametresiyle gönderir
                if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
                {
                    System.Diagnostics.Debug.WriteLine("[GoogleAuthService] 'code' parametresi bulunamadı.");
                    return null;
                }

                return new GoogleOAuthResult(code, codeVerifier, RedirectUri);
            }
            catch (TaskCanceledException)
            {
                // Kullanıcı tarayıcıyı kapattı — sessizce null dön
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GoogleAuthService] Hata: {ex.Message}");
                return null;
            }
        }

        // ── URL İnşası ────────────────────────────────────────────────────────────

        private static string BuildAuthUrl(string codeChallenge, string state)
        {
            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = WebClientId,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["scope"] = Scope,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["state"] = state,
                ["access_type"] = "online"
            };

            var query = string.Join("&",
                parameters.Select(kv =>
                    $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            return $"{AuthEndpoint}?{query}";
        }

        // ── PKCE Yardımcıları (RFC 7636) ─────────────────────────────────────────

        private static string GenerateCodeVerifier() => GenerateRandomBase64Url(32);

        private static string GenerateCodeChallenge(string codeVerifier)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier));
            return Base64UrlEncode(bytes);
        }

        private static string GenerateRandomBase64Url(int byteCount)
        {
            var bytes = new byte[byteCount];
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string Base64UrlEncode(byte[] input) =>
            Convert.ToBase64String(input)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
    }
}
