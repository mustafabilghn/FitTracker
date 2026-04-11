using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FitTrackr.API.Services
{
    public class AiWorkoutCoachService : IAiWorkoutCoachService
    {
        private readonly HttpClient _httpClient;
        private readonly IWorkoutAnalysisService _workoutAnalysisService;
        private readonly string _groqApiKey;
        private readonly string _groqModel;

        public AiWorkoutCoachService(
            HttpClient httpClient,
            IWorkoutAnalysisService workoutAnalysisService,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _workoutAnalysisService = workoutAnalysisService;
            _groqApiKey = configuration["Groq:ApiKey"] ?? string.Empty;
            _groqModel = configuration["Groq:Model"] ?? "llama-3.1-8b-instant";
        }

        public async Task<AiWorkoutInsightDto> GetInsightsAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new AiWorkoutInsightDto();
            }

            if (string.IsNullOrWhiteSpace(_groqApiKey) || string.IsNullOrWhiteSpace(_groqModel))
            {
                throw new InvalidOperationException("Groq configuration is missing. Please set Groq:ApiKey and Groq:Model.");
            }

            var analysis = await _workoutAnalysisService.GetAnalysisAsync(userId);
            var analysisJson = JsonSerializer.Serialize(analysis);

            if (analysis.TotalWorkouts == 0)
            {
                return new AiWorkoutInsightDto
                {
                    Summary = "Henüz analiz yapacak kadar antrenman verisi yok.",
                    Strengths = new List<string>(),
                    Improvements = new List<string>
                    {
                        "Önce birkaç antrenman kaydı ekle. Veri arttıkça yorumlar daha anlamlı olur."
                    },
                    NextWorkoutSuggestion = "Kısa ve temel bir antrenman kaydıyla başlayabilirsin."
                };
            }

            var requestBody = new
            {
                model = _groqModel,
                temperature = 0.2,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content =
                            "Yapılandırılmış antrenman verisini analiz eden muhafazakâr bir fitness koçusun. " +
                            "Yalnızca verilen veriye dayan. Veri dışında varsayım yapma. " +
                            "Mümkünse gerçek sayıları ve net gözlemleri kullan. " +
                            "Tüm yanıt değerleri tamamen Türkçe ve doğal Türkçe olsun. İngilizce kelime veya yarı Türkçe yarı İngilizce ifade kullanma. " +
                            "Yanıtlar kısa, temiz, kullanıcı dostu ve robotik olmayan bir Türkçe ile yazılsın. " +
                            "Veri azsa bunu açıkça söyle. " +
                            "Düşük aktiviteyi övme. Aktivite düşükse bunu açık ve nötr şekilde belirt. " +
                            "Emin olmadığın çıkarımları iddialı şekilde yazma. " +
                            "Veri güçlü şekilde desteklemiyorsa balanced, consistent, well-rounded, plateau, progression, beginner gibi çıkarımlar kullanma. " +
                            "Güçlü yönler listesine sadece gerçekten olumlu ve doğrudan veriden desteklenen maddeleri ekle. " +
                            "Gereksiz süslü dil kullanma. Yorumlar veri odaklı ve temkinli olsun. " +
                            "SADECE geçerli JSON döndür. JSON anahtarları tam olarak şunlar olsun: summary, strengths, improvements, nextWorkoutSuggestion. " +
                            "summary ve nextWorkoutSuggestion string olsun. strengths ve improvements string dizileri olsun. " +
                            "En fazla 2 strengths ve 2 improvements maddesi üret."
                    },
                    new
                    {
                        role = "user",
                        content = $"Antrenman analiz verisi (JSON): {analysisJson}. Bu veriyi analiz et ve kurallara uyan kısa içgörüler üret."
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _groqApiKey);

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Groq API request failed with status code {response.StatusCode}: {responseContent}");
            }

            var chatResponse = JsonSerializer.Deserialize<GroqChatResponse>(responseContent);

            var modelJson = chatResponse?.Choices?[0]?.Message?.Content;
            if (string.IsNullOrWhiteSpace(modelJson))
            {
                return new AiWorkoutInsightDto();
            }

            using var document = JsonDocument.Parse(modelJson);
            var root = document.RootElement;

            return new AiWorkoutInsightDto
            {
                Summary = ReadStringValue(root, "summary"),
                Strengths = ReadStringArray(root, "strengths"),
                Improvements = ReadStringArray(root, "improvements"),
                NextWorkoutSuggestion = ReadStringValue(root, "nextWorkoutSuggestion")
            };
        }

        private static string ReadStringValue(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property))
            {
                return string.Empty;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                _ => property.ToString()
            };
        }

        private static List<string> ReadStringArray(JsonElement root, string propertyName)
        {
            var result = new List<string>();

            if (!root.TryGetProperty(propertyName, out var property))
            {
                return result;
            }

            if (property.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.EnumerateArray())
                {
                    result.Add(item.ValueKind == JsonValueKind.String
                        ? item.GetString() ?? string.Empty
                        : item.ToString());
                }

                return result;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                result.Add(property.GetString() ?? string.Empty);
                return result;
            }

            result.Add(property.ToString());
            return result;
        }

        private class GroqChatResponse
        {
            [JsonPropertyName("choices")]
            public List<GroqChoice> Choices { get; set; } = new();
        }

        private class GroqChoice
        {
            [JsonPropertyName("message")]
            public GroqMessage? Message { get; set; }
        }

        private class GroqMessage
        {
            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }
    }
}
