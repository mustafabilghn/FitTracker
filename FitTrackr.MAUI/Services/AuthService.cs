using FitTrackr.MAUI.Models.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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

            if(token != null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        }

        public async Task<string?> GetTokenAsync()
        {
            return await SecureStorage.GetAsync("jwt_token");
        }

        public void Logout()
        {
            SecureStorage.Remove("jwt_token");
        }
    }
}
