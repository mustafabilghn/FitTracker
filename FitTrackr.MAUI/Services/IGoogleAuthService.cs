namespace FitTrackr.MAUI.Services
{
    /// <summary>
    /// Google OAuth 2.0 PKCE akışının MAUI tarafındaki sonucunu taşır.
    /// Backend'e bu üç değer birlikte gönderilir; token exchange orada yapılır.
    /// </summary>
    public record GoogleOAuthResult(string Code, string CodeVerifier, string RedirectUri);

    /// <summary>
    /// Google OAuth 2.0 PKCE akışını başlatır ve authorization code döner.
    /// İleride Apple Sign In için benzer bir IAppleAuthService yazılabilir.
    /// </summary>
    public interface IGoogleAuthService
    {
        /// <summary>
        /// Sistem tarayıcısını açar, kullanıcı Google hesabını seçer.
        /// Başarılıysa (Code, CodeVerifier, RedirectUri) döner.
        /// İptal veya hata durumunda null döner.
        /// </summary>
        Task<GoogleOAuthResult?> GetAuthorizationCodeAsync();
    }
}
