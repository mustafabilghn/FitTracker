namespace FitTrackr.API.Models.DTO
{
    /// <summary>
    /// MAUI'nin Google OAuth PKCE akışından aldığı authorization code'u backend'e taşır.
    /// Backend, client_secret'ı güvenli şekilde tutarak token exchange'i kendisi yapar.
    /// id_token hiçbir zaman client'a gönderilmez.
    /// </summary>
    public class GoogleLoginRequestDto
    {
        /// <summary>WebAuthenticator'ın yakaladığı Google authorization code.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>PKCE doğrulaması için MAUI tarafından üretilen code_verifier.</summary>
        public string CodeVerifier { get; set; } = string.Empty;

        /// <summary>Auth URL'de kullanılan redirect_uri — token exchange'de birebir aynı olmalı.</summary>
        public string RedirectUri { get; set; } = string.Empty;
    }
}
