using FitTrackr.MAUI.Models.DTO;
using System.Text.Json;

namespace FitTrackr.MAUI.Services
{
    public class WorkoutService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        public WorkoutService(HttpClient httpClient,JsonSerializerOptions jsonOptions)
        {
            _httpClient = httpClient;
            _jsonOptions = jsonOptions;
        }

        public async Task<List<WorkoutSummaryDto>> GetWorkoutsAsync()
        {
            var response = await _httpClient.GetAsync("api/workout");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<WorkoutSummaryDto>>(json,_jsonOptions) ?? new List<WorkoutSummaryDto>();
        }
    }
}
