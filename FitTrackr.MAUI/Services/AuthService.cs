using FitTrackr.MAUI.Models.DTO;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net.Http.Json;
using System.Text.Json;

namespace FitTrackr.MAUI.Services
{
    public class AuthService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions jsonSerializerOptions;

        public AuthService(HttpClient httpClient, JsonSerializerOptions jsonSerializerOptions)
        {
            _httpClient = httpClient;
            this.jsonSerializerOptions = jsonSerializerOptions;
        }

        public async Task<bool> RegisterAsync(string username, string email, string password)
        {
            var request = new
            {
                Username = username,
                Email = email,
                Password = password,
                Roles = new[] { "Reader", "Writer" }
            };

            var response = await _httpClient.PostAsJsonAsync("api/auth/register", request);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> LoginAsync(string email, string password)
        {
            var request = new { Email = email, Password = password };
            var response = await _httpClient.PostAsJsonAsync("api/auth/login", request);

            if (!response.IsSuccessStatusCode)
                return false;

            var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>(jsonSerializerOptions);

            if (result?.JwtToken == null)
                return false;

            await SecureStorage.SetAsync("jwt_token", result.JwtToken);

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.JwtToken);

            // JWT'den display name'i çıkar ve yerel önbelleğe kaydet
            ExtractAndCacheUserInfo(result.JwtToken, email);

            return true;
        }

        public async Task InitializeAsync()
        {
            var token = await SecureStorage.GetAsync("jwt_token");

            if (token != null)
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        }

        public async Task<string?> GetTokenAsync()
        {
            return await SecureStorage.GetAsync("jwt_token");
        }

        public async Task<UserProfileDto?> GetProfileAsync()
        {
            try
            {
                Debug.WriteLine("[AuthService] GetProfileAsync called");
                await InitializeAsync();

                var token = await GetTokenAsync();
                Debug.WriteLine($"[AuthService] Token exists: {!string.IsNullOrWhiteSpace(token)}");

                var response = await _httpClient.GetAsync("api/auth/profile");

                Debug.WriteLine($"[AuthService] GetProfile Status: {response.StatusCode}");
                var responseText = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[AuthService] GetProfile Response: {responseText}");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[AuthService] GetProfile failed");
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<UserProfileDto>(jsonSerializerOptions);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthService] GetProfileAsync error: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> UpdateProfileAsync(UpdateProfileRequestDto request)
        {
            try
            {
                Debug.WriteLine("[AuthService] UpdateProfileAsync called");
                await InitializeAsync();

                var token = await GetTokenAsync();
                Debug.WriteLine($"[AuthService] Token exists: {!string.IsNullOrWhiteSpace(token)}");
                Debug.WriteLine($"[AuthService] Auth header: {_httpClient.DefaultRequestHeaders.Authorization}");

                var url = "api/auth/profile";
                Debug.WriteLine($"[AuthService] URL: {_httpClient.BaseAddress}{url}");

                var json = JsonSerializer.Serialize(request, jsonSerializerOptions);
                Debug.WriteLine($"[AuthService] Request body: {json}");

                var response = await _httpClient.PutAsJsonAsync(url, request);

                Debug.WriteLine($"[AuthService] UpdateProfile Status: {response.StatusCode}");
                var responseText = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[AuthService] UpdateProfile Response: {responseText}");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[AuthService] UpdateProfile failed with status {response.StatusCode}");
                    return false;
                }

                Debug.WriteLine("[AuthService] UpdateProfile succeeded");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthService] UpdateProfileAsync error: {ex.Message}");
                Debug.WriteLine($"[AuthService] UpdateProfileAsync error stack: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<bool> ForgotPasswordAsync(string email)
        {
            try
            {
                var request = new { Email = email.Trim() };
                var response = await _httpClient.PostAsJsonAsync("api/auth/forgot-password", request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthService] ForgotPasswordAsync error: {ex.Message}");
                return false;
            }
        }

        public async Task<(bool Success, string? Error)> ResetPasswordAsync(string email, string code, string newPassword)
        {
            try
            {
                var request = new { Email = email.Trim(), Code = code.Trim(), NewPassword = newPassword };
                var response = await _httpClient.PostAsJsonAsync("api/auth/reset-password", request);

                if (response.IsSuccessStatusCode)
                    return (true, null);

                var errorText = await response.Content.ReadAsStringAsync();
                return (false, errorText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthService] ResetPasswordAsync error: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Google PKCE akışından gelen code + code_verifier'ı backend'e gönderir.
        /// Backend token exchange'i yapıp JWT döner; bu JWT SecureStorage'a kaydedilir.
        /// </summary>
        public async Task<bool> GoogleLoginAsync(string code, string codeVerifier, string redirectUri)
        {
            var request = new { Code = code, CodeVerifier = codeVerifier, RedirectUri = redirectUri };
            var response = await _httpClient.PostAsJsonAsync("api/auth/google-login", request);

            if (!response.IsSuccessStatusCode)
                return false;

            var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>(jsonSerializerOptions);

            if (result?.JwtToken == null)
                return false;

            await SecureStorage.SetAsync("jwt_token", result.JwtToken);

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.JwtToken);

            // E-posta dahil tüm bilgileri JWT'den çıkar (Google login'de e-posta parametre değil JWT'den gelir)
            ExtractAndCacheUserInfo(result.JwtToken, string.Empty);

            return true;
        }

        public void Logout()
        {
            SecureStorage.Remove("jwt_token");
            ClearLocalUserData();
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }

        private static void ClearLocalUserData()
        {
            Preferences.Remove("user_id");
            Preferences.Remove("username");
            Preferences.Remove("user_email");
            Preferences.Remove("height");
            Preferences.Remove("weight");
            Preferences.Remove("gender");
            Preferences.Remove("goal");

            try
            {
                var avatarDirectory = Path.Combine(FileSystem.AppDataDirectory, "profile");
                if (Directory.Exists(avatarDirectory))
                    Directory.Delete(avatarDirectory, recursive: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthService] Avatar temizleme hatası: {ex.Message}");
            }
        }

        public async Task<bool> DeleteAccountAsync()
        {
            try
            {
                await InitializeAsync();

                var response = await _httpClient.DeleteAsync("api/auth/delete-account");

                if (response.IsSuccessStatusCode)
                {
                    Logout();
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // JWT claim'lerinden userId, display name ve e-postayı Preferences'a yazar.
        // Farklı bir kullanıcı giriş yaparsa önceki veriler otomatik temizlenir.
        // email parametresi boşsa (Google login) e-posta JWT'den okunur.
        private static void ExtractAndCacheUserInfo(string jwtToken, string email)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(jwtToken);

                var newUserId = jwt.Claims
                    .FirstOrDefault(c => c.Type == "nameid")?.Value ?? string.Empty;

                // Farklı hesap giriş yapıyorsa önceki kullanıcının yerel verilerini temizle
                var storedUserId = Preferences.Get("user_id", string.Empty);
                if (!string.IsNullOrWhiteSpace(newUserId) &&
                    !string.IsNullOrWhiteSpace(storedUserId) &&
                    storedUserId != newUserId)
                {
                    Debug.WriteLine("[AuthService] Farklı kullanıcı tespit edildi, yerel veriler temizleniyor.");
                    ClearLocalUserData();
                }

                if (!string.IsNullOrWhiteSpace(newUserId))
                    Preferences.Set("user_id", newUserId);

                var displayName = jwt.Claims
                    .FirstOrDefault(c => c.Type == "unique_name")?.Value;

                if (!string.IsNullOrWhiteSpace(displayName))
                    Preferences.Set("username", displayName);

                if (!string.IsNullOrWhiteSpace(email))
                {
                    Preferences.Set("user_email", email);
                }
                else
                {
                    // Google login: e-postayı JWT'deki "email" claim'inden çıkar
                    var jwtEmail = jwt.Claims
                        .FirstOrDefault(c => c.Type == "email")?.Value;
                    if (!string.IsNullOrWhiteSpace(jwtEmail))
                        Preferences.Set("user_email", jwtEmail);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AuthService] ExtractAndCacheUserInfo error: {ex.Message}");
            }
        }
    }
}
