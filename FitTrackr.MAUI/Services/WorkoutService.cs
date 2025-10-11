using FitTrackr.MAUI.Models.DTO;
using System.Net.Http.Json;
using System.Text.Json;

namespace FitTrackr.MAUI.Services
{
    public class WorkoutService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        public WorkoutService(HttpClient httpClient, JsonSerializerOptions jsonOptions)
        {
            _httpClient = httpClient;
            _jsonOptions = jsonOptions;
        }

        public async Task<List<WorkoutSummaryDto>> GetWorkoutsAsync()
        {
            var response = await _httpClient.GetAsync("api/workout");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<WorkoutSummaryDto>>(json, _jsonOptions) ?? new List<WorkoutSummaryDto>();
        }

        public async Task<WorkoutSummaryDto> AddWorkoutAsync(WorkoutRequestDto workout)
        {
            var response = await _httpClient.PostAsJsonAsync("api/workout", workout);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<WorkoutSummaryDto>(_jsonOptions) ?? new WorkoutSummaryDto();
        }

        public async Task<List<LocationDto>> GetLocationsAsync()
        {
            var response = await _httpClient.GetAsync("api/location");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<LocationDto>>(json, _jsonOptions) ?? new List<LocationDto>();
        }

        public async Task<WorkoutDto> DeleteWorkoutAsync(Guid id)
        {
            var response = await _httpClient.DeleteAsync($"api/workout/{id}");
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<WorkoutDto>(_jsonOptions) ?? new WorkoutDto();
        }
    }
}
