using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace FitTrackr.API.Services
{
    public class AiWorkoutCoachService : IAiWorkoutCoachService
    {
        private readonly HttpClient _httpClient;
        private readonly IWorkoutAnalysisService _workoutAnalysisService;
        private readonly IMemoryCache _cache;
        private readonly string _groqApiKey;
        private readonly string _groqModel;

        private static readonly TimeSpan ContextCacheDuration = TimeSpan.FromMinutes(5);
        private const string ContextCacheKeyPrefix = "fitbot:ctx:";

        public AiWorkoutCoachService(
            HttpClient httpClient,
            IWorkoutAnalysisService workoutAnalysisService,
            IMemoryCache cache,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _workoutAnalysisService = workoutAnalysisService;
            _cache = cache;
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

        public async Task<FitBotChatResponseDto> ChatAsync(string userId, FitBotChatRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return new FitBotChatResponseDto { Reply = "Kullanıcı bilgisi alınamadı." };

            if (string.IsNullOrWhiteSpace(_groqApiKey))
                throw new InvalidOperationException("Groq yapılandırması eksik.");

            var cacheKey = $"{ContextCacheKeyPrefix}{userId}";
            if (!_cache.TryGetValue(cacheKey, out FitBotContextDto context))
            {
                context = await _workoutAnalysisService.GetFitBotContextAsync(userId);
                _cache.Set(cacheKey, context, ContextCacheDuration);
            }

            var systemPrompt = BuildSystemPrompt(request.ActionType, context);

            var messages = new List<object> { new { role = "system", content = systemPrompt } };

            var history = (request.ConversationHistory ?? new List<FitBotConversationMessageDto>())
                .TakeLast(10);

            foreach (var msg in history)
            {
                var role = string.Equals(msg.Role, "user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant";
                messages.Add(new { role, content = msg.Content });
            }

            messages.Add(new { role = "user", content = request.Message });

            var temperature = string.Equals(request.ActionType, "motivation", StringComparison.OrdinalIgnoreCase)
                ? 0.6
                : 0.3;

            var requestBody = new
            {
                model = _groqModel,
                temperature,
                messages = messages.ToArray()
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _groqApiKey);

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Groq API hatası {response.StatusCode}: {responseContent}");

            var chatResponse = JsonSerializer.Deserialize<GroqChatResponse>(responseContent);
            var rawReply = chatResponse?.Choices?[0]?.Message?.Content ?? string.Empty;
            var reply = SanitizeForeignWords(rawReply);
            if (string.Equals(request.ActionType, "motivation", StringComparison.OrdinalIgnoreCase))
                reply = TruncateToSentences(reply, 4);

            return new FitBotChatResponseDto
            {
                Reply = reply,
                PlateauAlerts = context.PlateauExercises
            };
        }

        private static string BuildSystemPrompt(string actionType, FitBotContextDto context)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Sen FitBot'sun — kullanıcının kişisel AI fitness koçu.");
            sb.AppendLine();
            sb.AppendLine("DİL KURALI — EN ÖNCELİKLİ KURAL:");
            sb.AppendLine("TÜM YANIT YALNIZCA TÜRKÇE olmalı. Çince (主要, 今日 gibi), Endonezce, İngilizce, İspanyolca (necesario gibi) veya başka herhangi bir dil KESİNLİKLE YASAK — tek bir karakter bile.");
            sb.AppendLine("Egzersiz ve hareket isimleri (Bench Press, Lat Pulldown, Pull-up, T-bar Row gibi) orijinal adlarıyla yaz. Türkçeye çevirme.");
            sb.AppendLine();
            sb.AppendLine("ZORUNLU KURALLAR:");
            sb.AppendLine("- Kullanıcıya HER ZAMAN 'sen/seni/senin' ile hitap et. 'Siz/sizin' YASAK.");
            sb.AppendLine("- Doğal Türkçe konuş. Aynı cümleyi veya ifadeyi birden fazla kez KULLANMA.");
            sb.AppendLine("- Yalnızca aşağıdaki verilere dayan. Uydurma, tahmin yürütme.");
            sb.AppendLine("- 'TREND' etiketleri kesindir; sayılara bakıp kendi yorumunu yapma.");
            sb.AppendLine($"- Takılma noktası listesi {(context.PlateauExercises.Count == 0 ? "BOŞ — 'plato/takılma' kelimesini KULLANMA." : "dolu — yalnızca listede olan egzersizler için kullan.")}");
            sb.AppendLine("- Veride olmayan bir başarı veya ilerlemeyi olumlu şekilde sunma.");
            sb.AppendLine("- Uygulamada haftalık antrenman hedefi YOKTUR. 'Bu hafta hedefini tamamladın', 'tüm antrenmanlarını tamamladın' gibi hedef başarısı ifadeleri YASAK.");
            sb.AppendLine();

            sb.AppendLine("=== KULLANICI VERİSİ ===");
            sb.AppendLine($"Toplam antrenman sayısı: {context.TotalWorkouts}");

            if (context.DaysSinceLastWorkout >= 0)
                sb.AppendLine($"Son antrenman kaç gün önce: {context.DaysSinceLastWorkout}");
            else
                sb.AppendLine("Son antrenman: Hiç kayıt yok.");

            sb.AppendLine($"Bu hafta yapılan antrenman: {context.WorkoutsThisWeek}");
            sb.AppendLine($"Son 30 günde yapılan antrenman: {context.WorkoutsLast30Days}");

            if (context.MuscleGroupFrequency.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Kas grubu çalışma sıklığı (son 30 gün):");
                foreach (var kv in context.MuscleGroupFrequency.OrderByDescending(kv => kv.Value))
                    sb.AppendLine($"  {kv.Key}: {kv.Value} antrenman");
            }

            if (context.RecentWorkouts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Son antrenmanlar — LİSTE EN YENİDEN ESKİYE DOĞRU SIRALI (ilk satır = EN SON antrenman):");
                var index = 1;
                foreach (var workout in context.RecentWorkouts)
                {
                    var recency = index == 1 ? "EN SON ANTRENMAN" : $"{index}. önceki antrenman";
                    sb.AppendLine($"  [{recency}] {workout.WorkoutDate:dd.MM.yyyy} — {workout.WorkoutName}");
                    foreach (var ex in workout.Exercises)
                    {
                        if (ex.MaxWeightKg > 0)
                            sb.AppendLine($"    • {ex.ExerciseName}: {ex.SetCount} set, bu seansın maks ağırlığı {ex.MaxWeightKg:F1} kg");
                        else
                            sb.AppendLine($"    • {ex.ExerciseName}: {ex.SetCount} set (ağırlık kaydedilmemiş)");
                    }
                    index++;
                }
            }

            if (context.WeightTrends.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Ağırlık trendleri (TREND etiketi kesin, değiştirme):");
                foreach (var trend in context.WeightTrends)
                {
                    var ordered = trend.WeeklyMaxWeights.OrderByDescending(w => w.WeeksAgo).ToList();
                    var oldest = ordered.First();
                    var newest = ordered.Last();
                    var delta = newest.MaxKg - oldest.MaxKg;
                    var deltaStr = delta >= 0 ? $"+{delta:F1}" : $"{delta:F1}";
                    sb.AppendLine($"  {trend.ExerciseName}: {oldest.MaxKg:F1} kg'dan {newest.MaxKg:F1} kg'a ({deltaStr} kg) — TREND: {trend.Trend.ToUpperInvariant()}");
                }
            }

            if (context.PlateauExercises.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Takılma noktasına giren egzersizler (son 3 haftadır maks ağırlık değişmedi):");
                foreach (var ex in context.PlateauExercises)
                    sb.AppendLine($"  - {ex}");
                sb.AppendLine("Yalnızca bu egzersizler için somut çıkış yolu öner: deload, rep değişimi veya egzersiz varyasyonu.");
            }

            sb.AppendLine();
            sb.AppendLine("=== YANIT TARZI ===");

            switch ((actionType ?? "free").ToLowerInvariant())
            {
                case "analyze":
                    sb.AppendLine("Antrenman verisini analiz et. Her noktayı FARKLI bir cümle yapısıyla anlat — tekrar etme.");
                    sb.AppendLine("Güçlü yönler: sadece veriden desteklenen gerçek gelişmeler. Gelişim alanları: eksik kas grupları, düşük frekans.");
                    sb.AppendLine("Takılma noktası listesi boşsa bu konuya GİRME. 4-5 farklı madde veya paragraf yeterli.");
                    break;
                case "today":
                    if (context.RecentWorkouts.Count > 0)
                    {
                        var lastWorkout = context.RecentWorkouts[0];
                        var lastExNames = string.Join(", ", lastWorkout.Exercises.Select(e => e.ExerciseName));
                        sb.AppendLine($"⛔ BUGÜN YASAK: Son antrenmanda ({lastWorkout.WorkoutDate:dd.MM.yyyy}) şu egzersizler yapıldı: [{lastExNames}]");
                        sb.AppendLine("Bu egzersizlerle AYNI kas grubuna giren hareketleri ÖNERME. Tamamen farklı bir kas grubunu seç.");
                    }
                    sb.AppendLine("TAM OLARAK 1 kas grubu seç ve commit et. 'Omuz veya sırt' gibi seçenek sunma.");
                    sb.AppendLine("Seçtiğin kas grubu için 2-3 hareket öner. Her harekete set ve tekrar sayısı ekle (örn: 'Squat: 3 set × 10 tekrar').");
                    sb.AppendLine("Hareket adlarını orijinal fitness terminolojisiyle yaz (örn: Pull-up, Cable Row, T-bar Row). Türkçe karşılık UYDURMA.");
                    sb.AppendLine("Birinci şahıs kullanma. 'Bugün X çalıştırmayı düşünüyorum' DEĞİL, 'Bugün X çalıştırmanı öneririm' şeklinde yaz.");
                    sb.AppendLine("Öneriyi sun ve DUR. Egzersiz listesinden sonra ek açıklama, teşvik veya paragraf YAZMA.");
                    break;
                case "program":
                    sb.AppendLine("Haftalık frekans, kas grubu dengesi ve ağırlık trendini değerlendir.");
                    sb.AppendLine("Çalışılmayan kas gruplarını ve somut iyileştirme adımlarını belirt. 3-4 paragraf.");
                    sb.AppendLine("MARKDOWN YASAK: **, *, #, numaralı liste (1. 2. 3.), madde imi (- veya •) KULLANMA. Düz paragraf yaz.");
                    sb.AppendLine("HER ZAMAN ikinci tekil şahısla yaz: 'öneririm', 'yapabilirsin'. 'Sunabiliriz', 'yapabiliriz' gibi çoğul ifadeler YASAK.");
                    sb.AppendLine("VERİ OKUMA KURALI: 'Son 30 günde X antrenman' ifadesi haftada değil 30 günlük toplamı gösterir. 'Haftada X antrenman yapıyorsun' gibi yanlış çıkarım YAPMA.");
                    break;
                case "motivation":
                    sb.AppendLine("AMAÇ: Kullanıcıyı bugün antrenmana gitmeye isteklendirmek. Veri raporu veya analiz YAZMA.");
                    sb.AppendLine("YAPI: Tek blok düz metin. BAŞLIK, MADDE İŞARETİ, HEADER YASAK.");
                    sb.AppendLine("UZUNLUK: Tam olarak 3-4 CÜMLE. Cümleleri say, 4'ten fazlasını yazma.");
                    sb.AppendLine("TON: Sıcak, enerjik, doğrudan — sanki yanında duran bir koç gibi.");
                    sb.AppendLine("VERİ KULLANIMI: Veriden 1 somut gerçeği (kg artışı veya antrenman sıklığı) en fazla 1 cümlede geç, ama amacın rapor vermek değil bu gerçeği motivasyon için yakıt olarak kullanmak.");
                    sb.AppendLine("Kalan cümleler ileriye bak: bugün, bu hafta, bir sonraki adım.");
                    sb.AppendLine("YASAK: 'Sen yapabilirsin', 'potansiyelin sınırsız', 'katkıda bulunmuş olmalı', 'bir göstergesi' — analitik ve belirsiz dil YASAK.");
                    break;
                default:
                    sb.AppendLine("Kullanıcının sorusuna kısa ve doğal yanıt ver. Gerektiğinde veriye başvur.");
                    break;
            }

            sb.AppendLine();
            sb.AppendLine("=== YAZI ÖNCESİ SON KONTROL ===");
            sb.AppendLine("Yanıtını yazmadan önce şunu kendinle kontrol et:");
            sb.AppendLine("Türkçe olmayan TEK BİR kelime bile var mı? (Örn: 'siguientes', 'wichtig', 'beberapa', '主要', 'already', herhangi bir yabancı dil kelimesi)");
            sb.AppendLine("Varsa o kelimeyi Türkçesiyle DEĞIŞTIR. Yabancı kelimeyle hiçbir şekilde gönderme.");

            return sb.ToString().Trim();
        }

        // Motivasyon gibi kısa tutulması gereken yanıtları cümle sayısıyla kes.
        // Tarih formatlarındaki (15.05.2026) ve sayılardaki noktalar cümle sonu sayılmaz.
        private static string TruncateToSentences(string text, int maxSentences)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c is '!' or '?')
                {
                    count++;
                    if (count >= maxSentences)
                        return text[..(i + 1)].TrimEnd();
                }
                else if (c == '.')
                {
                    bool prevIsDigit = i > 0 && char.IsDigit(text[i - 1]);
                    bool nextIsDigit = i < text.Length - 1 && char.IsDigit(text[i + 1]);
                    if (prevIsDigit || nextIsDigit) continue; // tarih veya sayının parçası

                    count++;
                    if (count >= maxSentences)
                        return text[..(i + 1)].TrimEnd();
                }
            }
            return text;
        }

        // Bilinen yabancı bağlaç/dolgu kelimeleri için güvenli kelime sınırı kontrollü değiştirme.
        // Fitness terimleri (Pull-up, T-bar Row vb.) tam kelime eşleşmesi olmadığı için etkilenmez.
        private static string SanitizeForeignWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Yabancı kelime → Türkçe karşılığı
            var replacements = new (string Pattern, string Replacement)[]
            {
                // Almanca
                (@"\bwichtig\b", "önemli"),
                (@"\bmöglich\b", "mümkün"),
                (@"\bsehr\b", "çok"),
                (@"\bauch\b", "da/de"),
                (@"\baber\b", "ancak"),
                // İspanyolca / Portekizce
                (@"\bsiguientes\b", "aşağıdaki"),
                (@"\bsiguiente\b", "aşağıdaki"),
                (@"\bseguinte\b", "aşağıdaki"),
                (@"\bseguintes\b", "aşağıdaki"),
                (@"\bnecessário\b", "gerekli"),
                (@"\bnecessario\b", "gerekli"),
                (@"\bnécessaire\b", "gerekli"),
                (@"\btambién\b", "de/da"),
                (@"\bpero\b", "ancak"),
                (@"\bes decir\b", "yani"),
                // Fransızca
                (@"\baussi\b", "de/da"),
                (@"\bdonc\b", "bu nedenle"),
                (@"\bmais\b", "ancak"),
                // İtalyanca
                (@"\banche\b", "de/da"),
                (@"\bperò\b", "ancak"),
                // Endonezce/Malayca
                (@"\bbeberapa\b", "birkaç"),
                (@"\bseperti\b", "gibi"),
                (@"\bjuga\b", "de/da"),
                (@"\buntuk\b", "için"),
                // İngilizce dolgu kelimeleri (hareket isimleri değil)
                (@"\balready\b", "zaten"),
                (@"\bmostly\b", "çoğunlukla"),
                (@"\bhowever\b", "ancak"),
                (@"\btherefore\b", "bu nedenle"),
                (@"\boverall\b", "genel olarak"),
                (@"\bregularly\b", "düzenli olarak"),
                (@"\bnecessary\b", "gerekli"),
                (@"\bimportant\b", "önemli"),
                (@"\bessential\b", "gerekli"),
                // Slavik diller (Çekçe, Slovakça, Lehçe vb.)
                (@"\bpostupně\b", "kademeli olarak"),
                (@"\bpomalu\b", "yavaş yavaş"),
                (@"\bstopniowo\b", "kademeli olarak"),
                // Çince karakterler (sık görülen)
                (@"主要", "esas olarak"),
                (@"今日", "bugün"),
                (@"重要", "önemli"),
                (@"需要", "gerekli"),
                // Yaygın Çince blok (tek başına duran karakterler)
                (@"[一-鿿]+", ""),
                // İspanyolca (aksan olmayan formlar)
                (@"\bnecesario\b", "gerekli"),
                (@"\bnecesaria\b", "gerekli"),
                (@"\btambien\b", "de/da"),
                // Prompt'ta yasak ama model yine de yazabiliyor (tüm türetilmiş formları yakala)
                (@"potansiyel\w*\s+sınırsız", "güçlün çok büyük"),
            };

            foreach (var (pattern, replacement) in replacements)
                text = System.Text.RegularExpressions.Regex.Replace(text, pattern, replacement,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return text;
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
