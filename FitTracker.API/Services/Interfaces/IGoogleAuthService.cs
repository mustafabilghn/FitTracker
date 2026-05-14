using Google.Apis.Auth;

namespace FitTrackr.API.Services.Interfaces
{
    /// <summary>
    /// Google OAuth 2.0 authorization code akışı için sözleşme.
    /// Backend, PKCE code_verifier ve client_secret ile token exchange yapar.
    /// İleride Apple Sign In için benzer bir IAppleAuthService yazılabilir.
    /// </summary>
    public interface IGoogleAuthService
    {
        /// <summary>
        /// Google'dan alınan authorization code'u id_token ile değiştirir ve doğrular.
        /// </summary>
        /// <param name="code">MAUI'nin WebAuthenticator'dan aldığı authorization code.</param>
        /// <param name="codeVerifier">PKCE code_verifier (client'ta üretildi).</param>
        /// <param name="redirectUri">Auth URL'de kullanılan redirect URI — birebir aynı olmalı.</param>
        Task<GoogleJsonWebSignature.Payload> ExchangeAndValidateCodeAsync(
            string code,
            string codeVerifier,
            string redirectUri);
    }
}
