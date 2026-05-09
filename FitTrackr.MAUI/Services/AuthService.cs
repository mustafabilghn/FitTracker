using FitTrackr.MAUI.Models.DTO;
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;

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

        public async Task<bool> RegisterAsync(string username, string password)
        {
            var request = new { Username = username, Password = password, Roles = new[] { "Reader", "Writer" } };
            var response = await _httpClient.PostAsJsonAsync("api/auth/register", request);

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            var request = new { Username = username, Password = password };
            var response = await _httpClient.PostAsJsonAsync("api/auth/login", request);

            if (!response.IsSuccessStatusCode)
                return false;

            var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>(jsonSerializerOptions);

            if (result?.JwtToken == null)
                return false;

            await SecureStorage.SetAsync("jwt_token", result.JwtToken);

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.JwtToken);

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

                // Request body'yi log et
                var json = System.Text.Json.JsonSerializer.Serialize(request, jsonSerializerOptions);
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

        public void Logout()
        {
            SecureStorage.Remove("jwt_token");
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
    }
}
