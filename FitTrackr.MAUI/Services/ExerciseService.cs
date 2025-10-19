using FitTrackr.MAUI.Models.DTO;
using System.Net.Http.Json;
using System.Text.Json;

namespace FitTrackr.MAUI.Services
{
    public class ExerciseService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        public ExerciseService(HttpClient httpClient, JsonSerializerOptions jsonOptions)
        {
            _httpClient = httpClient;
            _jsonOptions = jsonOptions;
        }

        public async Task<ExerciseDto> AddExerciseAsync(ExerciseRequestDto request)
        {
            var response = await _httpClient.PostAsJsonAsync("api/exercise", request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<ExerciseDto>(_jsonOptions) ?? new ExerciseDto();
        }

        public async Task<List<IntensityDto>> GetIntensitiesAsync()
        {
            var response = await _httpClient.GetAsync("api/intensity");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<IntensityDto>>(json, _jsonOptions) ?? new List<IntensityDto>();
        }
    }
}
