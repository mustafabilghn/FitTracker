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
    public class ExerciseSetService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        public ExerciseSetService(HttpClient httpClient, JsonSerializerOptions jsonSerializerOptions)
        {
            _httpClient = httpClient;
            _jsonOptions = jsonSerializerOptions;
        }

        public async Task<ExerciseSetDto?> AddSetAsync(ExerciseSetRequestDto exerciseSetRequestDto)
        {
            var response = await _httpClient.PostAsJsonAsync("api/exercisesets", exerciseSetRequestDto);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<ExerciseSetDto>(_jsonOptions);
        }

        public async Task<ExerciseSetDto> DeleteSetAsync(Guid id)
        {
            var response = await _httpClient.DeleteAsync($"api/exercisesets/{id}");
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<ExerciseSetDto>(_jsonOptions);
        }
    }
}
