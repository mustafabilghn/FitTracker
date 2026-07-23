using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FitTrackr.API.Services
{
    public class AiWorkoutCoachService : IAiWorkoutCoachService
    {
        private readonly HttpClient _httpClient;
        private readonly IWorkoutAnalysisService _workoutAnalysisService;
        private readonly IAcsmGuardrailService _guardrailService;
        private readonly IMemoryCache _cache;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AiWorkoutCoachService> _logger;
        private readonly string _groqApiKey;
        private readonly string _groqModel;

        private static readonly TimeSpan ContextCacheDuration = TimeSpan.FromMinutes(1);
        private const string ContextCacheKeyPrefix = "fitbot:ctx:";

        // Ölçüm amaçlı response header adları. Yalnızca timing/telemetri taşır;
        // yanıt gövdesini veya iş mantığını etkilemez. Bkz. evaluation/benchmark/README.md.
        private const string ContextMsHeader = "X-FitBot-Context-Ms";
        private const string ContextCacheHeader = "X-FitBot-Context-Cache";
        private const string GroqMsHeader = "X-FitBot-Groq-Ms";

        // İsteğin dili RequestLocalizationMiddleware tarafından Accept-Language header'ından
        // çözümlenip CultureInfo.CurrentUICulture'a yazılır (bkz. Program.cs). Buradan okunur.
        private static bool IsEnglish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

        public AiWorkoutCoachService(
            HttpClient httpClient,
            IWorkoutAnalysisService workoutAnalysisService,
            IAcsmGuardrailService guardrailService,
            IMemoryCache cache,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AiWorkoutCoachService> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _workoutAnalysisService = workoutAnalysisService;
            _guardrailService = guardrailService;
            _cache = cache;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
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
                return IsEnglish
                    ? new AiWorkoutInsightDto
                    {
                        Summary = "Not enough workout data to analyze yet.",
                        Strengths = new List<string>(),
                        Improvements = new List<string>
                        {
                            "Log a few workouts first — insights become more meaningful as your data grows."
                        },
                        NextWorkoutSuggestion = "Start by logging a short, simple workout."
                    }
                    : new AiWorkoutInsightDto
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

            var systemContent = IsEnglish
                ? "You are a conservative fitness coach who analyzes structured workout data. " +
                  "Base your analysis only on the data provided. Do not make assumptions beyond the data. " +
                  "Use real numbers and clear observations wherever possible. " +
                  "Respond only in English. Use clear, concise, supportive fitness-coach language. " +
                  "If data is limited, say so explicitly. " +
                  "Do not praise low activity. If activity is low, state it plainly and neutrally. " +
                  "Do not make confident claims when you are uncertain. " +
                  "Avoid inferences like 'balanced', 'consistent', 'well-rounded', 'plateau', 'progression', 'beginner' unless strongly supported by the data. " +
                  "Add to the strengths list only genuinely positive items directly supported by the data. " +
                  "Avoid flowery or filler language. Keep observations data-driven and cautious. " +
                  "Return ONLY valid JSON. JSON keys must be exactly: summary, strengths, improvements, nextWorkoutSuggestion. " +
                  "summary and nextWorkoutSuggestion must be strings. strengths and improvements must be string arrays. " +
                  "Generate at most 2 strengths and 2 improvements."
                : "Yapılandırılmış antrenman verisini analiz eden muhafazakâr bir fitness koçusun. " +
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
                  "En fazla 2 strengths ve 2 improvements maddesi üret.";

            var userContent = IsEnglish
                ? $"Workout analysis data (JSON): {analysisJson}. Analyze this data and produce concise insights following the rules above."
                : $"Antrenman analiz verisi (JSON): {analysisJson}. Bu veriyi analiz et ve kurallara uyan kısa içgörüler üret.";

            var requestBody = new
            {
                model = _groqModel,
                temperature = 0.2,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = systemContent },
                    new { role = "user", content = userContent }
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
                return new FitBotChatResponseDto { Reply = IsEnglish ? "Unable to identify user." : "Kullanıcı bilgisi alınamadı." };

            if (string.IsNullOrWhiteSpace(_groqApiKey))
                throw new InvalidOperationException(IsEnglish ? "Groq configuration is missing." : "Groq yapılandırması eksik.");

            var cacheKey = $"{ContextCacheKeyPrefix}{userId}";
            var contextStopwatch = Stopwatch.StartNew();
            var contextCacheHit = _cache.TryGetValue(cacheKey, out FitBotContextDto context);
            if (!contextCacheHit)
            {
                context = await _workoutAnalysisService.GetFitBotContextAsync(userId);
                _cache.Set(cacheKey, context, ContextCacheDuration);
            }
            contextStopwatch.Stop();

            var systemPrompt = BuildSystemPrompt(request.ActionType, context);

            var messages = new List<object> { new { role = "system", content = systemPrompt } };

            var history = (request.ConversationHistory ?? new List<FitBotConversationMessageDto>())
                .TakeLast(6);

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

            var groqStopwatch = Stopwatch.StartNew();
            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();
            groqStopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    RecordTimingTelemetry(contextStopwatch.ElapsedMilliseconds, contextCacheHit, groqStopwatch.ElapsedMilliseconds);
                    return new FitBotChatResponseDto
                    {
                        Reply = IsEnglish
                            ? "Too many requests right now — wait a moment and try again."
                            : "Şu an çok fazla istek var, biraz bekleyip tekrar dene.",
                        PlateauAlerts = new List<string>()
                    };
                }

                throw new InvalidOperationException(
                    IsEnglish
                        ? $"Groq API error {response.StatusCode}: {responseContent}"
                        : $"Groq API hatası {response.StatusCode}: {responseContent}");
            }

            var chatResponse = JsonSerializer.Deserialize<GroqChatResponse>(responseContent);
            var rawReply = chatResponse?.Choices?[0]?.Message?.Content ?? string.Empty;
            var reply = SanitizeForeignWords(rawReply);
            reply = SanitizeOutputPatterns(reply);
            if (string.Equals(request.ActionType, "motivation", StringComparison.OrdinalIgnoreCase))
                reply = TruncateToSentences(reply, 4);

            // ACSM progressive overload guardrail: cap any weight recommendation exceeding 10% of recent max
            var guardrailResult = _guardrailService.Validate(reply, context);

            RecordTimingTelemetry(contextStopwatch.ElapsedMilliseconds, contextCacheHit, groqStopwatch.ElapsedMilliseconds);

            return new FitBotChatResponseDto
            {
                Reply = guardrailResult.SanitizedReply,
                PlateauAlerts = context.PlateauExercises,
                GuardrailTriggered = guardrailResult.Triggered,
                InterceptedProgressions = guardrailResult.InterceptedProgressions.ToList()
            };
        }

        // Ölçüm/logging amaçlı: context ve Groq çağrı sürelerini response header'ları ve
        // yapılandırılmış bir log satırı olarak yayınlar. Yanıt gövdesini, durum kodunu veya
        // herhangi bir iş kuralını DEĞİŞTİRMEZ; header yazımı başarısız olursa sessizce yutulur.
        private void RecordTimingTelemetry(long contextMs, bool contextCacheHit, long groqMs)
        {
            _logger.LogInformation(
                "FitBotChat timing: cache={CacheStatus} contextMs={ContextMs} groqMs={GroqMs}",
                contextCacheHit ? "hit" : "miss", contextMs, groqMs);

            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext == null || httpContext.Response.HasStarted)
                    return;

                httpContext.Response.Headers[ContextMsHeader] = contextMs.ToString(CultureInfo.InvariantCulture);
                httpContext.Response.Headers[ContextCacheHeader] = contextCacheHit ? "hit" : "miss";
                httpContext.Response.Headers[GroqMsHeader] = groqMs.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                // Timing header'ları best-effort'tur; hiçbir koşulda API yanıtını bozmamalı.
            }
        }

        // Dil dispatcher'ı: prompt-mühendisliği metni (paper'ın güvenlik garantilerinin kalbi)
        // yanlışlıkla bozulmasın diye TR/EN için iki tam, bağımsız metot olarak tutulur.
        private static string BuildSystemPrompt(string actionType, FitBotContextDto context) =>
            IsEnglish ? BuildSystemPromptEnglish(actionType, context) : BuildSystemPromptTurkish(actionType, context);

        private static string BuildSystemPromptTurkish(string actionType, FitBotContextDto context)
        {
            var sb = new StringBuilder();

            // Veri yoksa sade, kısa ve uydurmasız yanıt ver
            if (context.TotalWorkouts == 0)
            {
                sb.AppendLine("Sen FitBot'sun — kullanıcının kişisel AI fitness koçu.");
                sb.AppendLine("Kullanıcının henüz hiç antrenman kaydı yok.");
                sb.AppendLine("KURAL: Olmayan veriyi UYDURMA. Geçmiş antrenman, ağırlık, kas grubu veya trend hakkında HİÇBİR spesifik bilgi verme.");
                sb.AppendLine("KURAL: 'sen', 'seni', 'senin' kullan. 'siz' YASAK.");
                sb.AppendLine();

                switch ((actionType ?? "free").ToLowerInvariant())
                {
                    case "analyze":
                    case "program":
                        sb.AppendLine("1-2 cümleyle 'henüz kayıt yok, antrenman eklemeye başla' de ve dur. Öneri veya analiz ekleme.");
                        break;
                    case "today":
                        sb.AppendLine("Kayıt olmadığı için herhangi bir kas grubunu öner. 2-3 hareket, her biri ayrı satırda, tam olarak bu formatta: 'HareketAdı: 3 set × 10 tekrar'. 'Hareket:' ön eki KULLANMA, doğrudan hareket adıyla başla.");
                        break;
                    case "motivation":
                        sb.AppendLine("Tam olarak 3 kısa cümle. İlk cümle doğrudan aksiyona odaklan. Spesifik ağırlık veya egzersiz ismi UYDURMA. Genel ama enerjik bir başlangıç motivasyonu yap.");
                        break;
                    default:
                        sb.AppendLine("Kullanıcının sorusuna kısa ve doğal yanıt ver. Olmayan veri uydurma.");
                        break;
                }

                return sb.ToString().Trim();
            }

            sb.AppendLine("Sen FitBot'sun — kullanıcının kişisel AI fitness koçu.");
            sb.AppendLine();
            sb.AppendLine("DİL KURALI — EN ÖNCELİKLİ KURAL:");
            sb.AppendLine("TÜM YANIT YALNIZCA TÜRKÇE olmalı. Çince (主要, 今日 gibi), Endonezce, İngilizce, İspanyolca (necesario gibi) veya başka herhangi bir dil KESİNLİKLE YASAK — tek bir karakter bile.");
            sb.AppendLine("Egzersiz ve hareket isimleri (Bench Press, Lat Pulldown, Pull-up, T-bar Row gibi) orijinal adlarıyla yaz. Türkçeye çevirme.");
            sb.AppendLine();
            sb.AppendLine("ZORUNLU KURALLAR:");
            sb.AppendLine("- Kullanıcıya HER ZAMAN 'sen/seni/senin' ile hitap et. 'Siz/sizin' YASAK.");
            sb.AppendLine("- BİZ FORMU KESİNLİKLE YASAK: 'görüyoruz', 'konuşabiliriz', 'yapabiliriz', 'izleyebiliriz', 'düşünüyoruz', 'değerlendirebiliriz' gibi TÜM çoğul fiiller YASAK. SADECE tekil: 'görüyorum', 'yapabilirsin', 'izleyebilirsin'.");
            sb.AppendLine("- SORU SORMAK YASAK: 'ister misin?', 'hakkında konuşmak ister misin?' gibi kullanıcıya soru yöneltme. Doğrudan analiz yap ve bitir.");
            sb.AppendLine("- AĞIRLIK SAYILARINDAN KİŞİLİK ÇIKARIMI YAPMA: '100→80→50 kg' gibi serilerden 'farklı zorluk seviyelerine alışkınsın', 'motivasyonun yüksek' gibi yorumlar YASAK. Sayılar sadece ağırlık kaydı.");
            sb.AppendLine("- Doğal Türkçe konuş. Aynı cümleyi veya ifadeyi birden fazla kez KULLANMA. Her paragraf/madde FARKLI bir fikir içermeli.");
            sb.AppendLine("- Yalnızca aşağıdaki verilere dayan. Uydurma, tahmin yürütme.");
            sb.AppendLine("- TREND KURALI: Yalnızca aşağıdaki 'Ağırlık trendleri' bölümünde adı geçen egzersizler için trend yaz. Bu bölüm BOŞ ise — konuşmada hiçbir egzersiz için 'ağırlık düştü/arttı/artış/düşüş' gibi trend ifadesi KESİNLİKLE KULLANMA. Son antrenman listesindeki ağırlık sayılarından kendi trend hesabını YAPMA.");
            sb.AppendLine("- AKTİVİTE KURALI: Veri satırında '← DÜŞÜK/ORTA' uyarısı olan antrenman sayısı için 'düzenli' veya 'düzenli antrenman yapıyorsun' DEME.");
            sb.AppendLine("- KARŞILAŞTIRMA KURALI: 'Sıklık arttı', 'gelişim gösteriyor', 'ilerleme var' gibi ifadeler için önceki döneme ait karşılaştırma verisi gerekir. Bu hafta ve son 30 gün sayısı eşit ya da yakınsa (tüm antrenmanlar bu haftada demektir) — 'arttı/gelişti' İFADELERİNİ KULLANMA.");
            sb.AppendLine("- ELİNDE OLMAYAN VERİYE İHTİYAÇ DUYDUĞUNU ASLA SÖYLEME. 'Daha fazla bilgiye/veriye ihtiyaç duyuluyor', 'analiz edilebilmesi için daha fazla veri gerekli', 'yoğunluk hakkında bilgim yok' gibi ifadeler KESİNLİKLE YASAK. Sadece mevcut veriyle çalış.");
            sb.AppendLine("- KURALLARI AÇIKLAMA: Neden bir konudan bahsetmediğini açıklama. 'Takılma noktası listesi boş olduğu için girilmeyecektir' gibi meta-yorumlar YASAK. Sadece bahsetme, açıklama yapma.");
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
            var activityNote = context.WorkoutsLast30Days <= 8 ? " ← DÜŞÜK/ORTA — 'düzenli', 'sık aralıklarla', 'düzenli olarak' DEME" : "";
            sb.AppendLine($"Son 30 günde yapılan antrenman: {context.WorkoutsLast30Days}{activityNote}");

            // Yalnızca birden fazla farklı antrenman adı varsa göster — tek tip isimde (ör. hepsi "Antrenman") model yanıltıcı çıkarım yapıyor
            var uniqueWorkoutNames = context.MuscleGroupFrequency.Keys.ToList();
            if (uniqueWorkoutNames.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("Antrenman türü dağılımı (son 30 gün):");
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
                sb.AppendLine("Ağırlık trendleri — bu satırlardaki bilgiyi olduğu gibi aktar, yorum ekleme:");
                foreach (var trend in context.WeightTrends)
                {
                    var ordered = trend.WeeklyMaxWeights.OrderByDescending(w => w.WeeksAgo).ToList();
                    var oldest = ordered.First();
                    var newest = ordered.Last();
                    var delta = newest.MaxKg - oldest.MaxKg;
                    var deltaStr = delta >= 0 ? $"+{delta:F1}" : $"{delta:F1}";
                    var trendDesc = trend.Trend.ToUpperInvariant() switch
                    {
                        "UP" => "son haftalarda ağırlık artıyor",
                        "DOWN" => "son haftalarda ağırlık düşüyor",
                        "STABLE" => "son haftalarda ağırlık sabit",
                        _ => trend.Trend.ToLowerInvariant()
                    };
                    sb.AppendLine($"  {trend.ExerciseName}: {oldest.MaxKg:F1} kg → {newest.MaxKg:F1} kg ({deltaStr} kg), {trendDesc}");
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
                    sb.AppendLine("Güçlü yönler: sadece veriden desteklenen gerçek gelişmeler. Toplam antrenman sayısı 5'ten azsa güçlü yön listesi OLUŞTURMA — 'veri henüz yetersiz' de.");
                    sb.AppendLine("Gelişim alanları: eksik kas grupları, düşük frekans. Takılma noktası listesi boşsa bu konuya GİRME. 3-4 farklı madde veya paragraf yeterli.");
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
                    sb.AppendLine("FORMAT: 1 kısa giriş cümlesi ('Bugün X çalıştırmanı öneririm.'), ardından TAM OLARAK 2-3 hareket, her biri ayrı satırda. 4 veya daha fazla hareket YAZMA. Listeden sonra EK CÜMLE YAZMA.");
                    sb.AppendLine("AĞIRLIK FORMATI: Eğer egzersiz için 'Ağırlık trendleri' bölümünde geçmiş veri mevcutsa, satırı şu formatta yaz: 'HareketAdı: X set × Y tekrar @ Z kg'. Ağırlık verisi yoksa sadece: 'HareketAdı: X set × Y tekrar'.");
                    sb.AppendLine("ACSM GÜVENLİK KURALI: Önerilen ağırlık (Z kg), o egzersizin son haftaki maksimum ağırlığının %110'unu geçmemeli (ACSM progressive overload ≤10%/hafta).");
                    sb.AppendLine("'Birkaç egzersiz önerisi yapabilirim', 'bazı egzersizler önerebilirim' gibi belirsiz giriş cümleleri YASAK — direkt kas grubunu söyle.");
                    sb.AppendLine("Hareket adlarını orijinal fitness terminolojisiyle yaz (Pull-up, Cable Row, T-bar Row). Türkçe karşılık UYDURMA.");
                    break;
                case "program":
                    sb.AppendLine("Haftalık frekans, kas grubu dengesi ve ağırlık trendini değerlendir.");
                    sb.AppendLine("Her paragraf FARKLI bir konu içermeli: 1.paragraf frekans, 2.paragraf kas dengesi, 3.paragraf trend, 4.paragraf somut öneri. Aynı fikri iki paragrafta tekrarlama.");
                    sb.AppendLine("SON PARAGRAF KURALI: 'Sonuç olarak', 'özetle', 'bu nedenle' ile BAŞLAMA. Son paragraf öncekini tekrarlamamalı — veriden desteklenen tek somut adımı içermeli.");
                    sb.AppendLine("EGZERSİZ LİSTESİ YASAK: 'Squat: 3 set × 10 tekrar' gibi set/tekrar içeren egzersiz planı program değerlendirmesine EKLEME.");
                    sb.AppendLine("MARKDOWN YASAK: **, *, #, numaralı liste (1. 2. 3.), madde imi (- veya •) KULLANMA. Düz paragraf yaz.");
                    sb.AppendLine("HER ZAMAN ikinci tekil şahısla yaz: 'öneririm', 'yapabilirsin', 'izleyebilirsin'. 'Sunabiliriz', 'yapabiliriz', 'izleyebiliriz', 'seviyenizi' gibi çoğul/resmi ifadeler YASAK.");
                    sb.AppendLine("VERİ OKUMA KURALI: 'Son 30 günde X antrenman' ifadesi haftada değil 30 günlük toplamı gösterir. 'Haftada X antrenman yapıyorsun' gibi yanlış çıkarım YAPMA.");
                    sb.AppendLine("TREND KURALI: Yalnızca ağırlık trendleri bölümünde yer alan egzersizler için trend yorumu yap. Listede olmayan egzersizler için 'trendini izle', 'takip et' gibi öneriler YAPMA.");
                    break;
                case "motivation":
                    sb.AppendLine("AMAÇ: Kullanıcıyı bugün antrenmana gitmeye isteklendirmek.");
                    sb.AppendLine("YAPI: Tam olarak 3 KISA CÜMLE. Her cümle tek fikir. Düz metin.");
                    sb.AppendLine("TON: Koç tonu — kısa, sert, enerjik.");
                    sb.AppendLine("İLK KELİME: Yanıt 'Hadi', 'Bugün', doğrudan bir eylem veya somut sayı ile başlamalı. 'Seni', 'Senin', 'Beni', 'Sizi' ile BAŞLAMA.");
                    sb.AppendLine("VERİ: Veriden 1 somut sayı kullan (kg veya gün sayısı).");
                    sb.AppendLine("YASAK: '...istiyorum', '...çağırmak', '...etmeli/etmelidir', 'hatırla' (nostalji tonu).");
                    sb.AppendLine("ÖRNEK: 'Hadi salona git, kasların hazır. 100 kg'a ulaştın, bugün üstüne koy. Hareket et.'");
                    break;
                default:
                    sb.AppendLine("Kullanıcının sorusuna kısa ve doğal yanıt ver. Gerektiğinde veriye başvur.");
                    break;
            }

            sb.AppendLine();
            sb.AppendLine("=== YAZI ÖNCESİ SON KONTROL ===");
            sb.AppendLine("Yanıtını göndermeden önce şu üç soruyu sor:");
            sb.AppendLine("1. Türkçe olmayan TEK BİR kelime var mı? Varsa Türkçesiyle değiştir.");
            sb.AppendLine("2. 'Daha fazla bilgiye ihtiyacım var' veya benzer bir ifade var mı? Varsa sil — eldeki veriyle çalış.");
            sb.AppendLine("3. Aynı fikri iki farklı cümleyle mi tekrarladım? Varsa birini sil.");

            return sb.ToString().Trim();
        }

        private static string BuildSystemPromptEnglish(string actionType, FitBotContextDto context)
        {
            var sb = new StringBuilder();

            if (context.TotalWorkouts == 0)
            {
                sb.AppendLine("You are FitBot — the user's personal AI fitness coach.");
                sb.AppendLine("The user has no workout records yet.");
                sb.AppendLine("RULE: Do NOT fabricate data. Give NO specific information about past workouts, weights, muscle groups, or trends.");
                sb.AppendLine("LANGUAGE RULE — HIGHEST PRIORITY: Respond only in English. Use clear, concise, supportive fitness-coach language.");
                sb.AppendLine();

                switch ((actionType ?? "free").ToLowerInvariant())
                {
                    case "analyze":
                    case "program":
                        sb.AppendLine("In 1-2 sentences, say 'no records yet — start logging workouts' and stop. Do not add suggestions or analysis.");
                        break;
                    case "today":
                        sb.AppendLine("Since there are no records, suggest any muscle group. 2-3 exercises, each on its own line, in exactly this format: 'ExerciseName: 3 sets × 10 reps'. Do NOT use a 'Exercise:' prefix — start directly with the exercise name.");
                        break;
                    case "motivation":
                        sb.AppendLine("Exactly 3 short sentences. First sentence: direct call to action. Do NOT fabricate specific weights or exercise names. Keep it general but energetic.");
                        break;
                    default:
                        sb.AppendLine("Answer the user's question briefly and naturally. Do not fabricate missing data.");
                        break;
                }

                return sb.ToString().Trim();
            }

            sb.AppendLine("You are FitBot — the user's personal AI fitness coach.");
            sb.AppendLine();
            sb.AppendLine("LANGUAGE RULE — HIGHEST PRIORITY:");
            sb.AppendLine("Respond only in English. Use clear, concise, supportive fitness-coach language. Any other language is strictly forbidden.");
            sb.AppendLine("Exercise and movement names (Bench Press, Lat Pulldown, Pull-up, T-bar Row, etc.) should be written in their original form.");
            sb.AppendLine();
            sb.AppendLine("MANDATORY RULES:");
            sb.AppendLine("- Always address the user as 'you/your'. Never use 'we' forms like 'we can see', 'we can discuss', 'we can do'.");
            sb.AppendLine("- DO NOT ASK QUESTIONS: Do not ask the user things like 'would you like to...?'. Directly analyze and conclude.");
            sb.AppendLine("- DO NOT infer personality from weight numbers: sequences like '100→80→50 kg' must not be interpreted as 'you are used to different difficulty levels'. Numbers are just weight records.");
            sb.AppendLine("- Speak naturally. Do not repeat the same sentence or phrase more than once. Each paragraph/bullet must contain a DIFFERENT idea.");
            sb.AppendLine("- Base responses only on the data below. Do not fabricate or speculate.");
            sb.AppendLine("- TREND RULE: Write about weight trends ONLY for exercises explicitly listed in the 'Weight trends' section below. If that section is EMPTY — do NOT use phrases like 'weight dropped/increased/rising/falling' for any exercise. Do NOT compute your own trends from the recent workout weights.");
            sb.AppendLine("- ACTIVITY RULE: If the workout count line is marked '← LOW/MODERATE', do NOT say 'consistent' or 'you train consistently'.");
            sb.AppendLine("- COMPARISON RULE: Phrases like 'frequency increased', 'showing progress', 'improvement' require comparative data from a prior period. If this week and the last-30-days count are equal or close (meaning all workouts happened this week), do NOT use improvement language.");
            sb.AppendLine("- NEVER say you need more data. Phrases like 'more data is needed', 'I need more information', 'I don't know the intensity' are strictly forbidden. Work only with what is available.");
            sb.AppendLine("- DO NOT EXPLAIN YOUR RULES: Do not explain why you are not covering a topic. Meta-comments like 'I will skip the plateau section since the list is empty' are forbidden. Simply don't mention it.");
            sb.AppendLine($"- Plateau list is {(context.PlateauExercises.Count == 0 ? "EMPTY — do NOT use the word 'plateau'." : "populated — only reference exercises that appear in it.")}");
            sb.AppendLine("- Do not present an achievement or progress that is not in the data.");
            sb.AppendLine("- The app has NO weekly workout goal. Do not say 'you hit your goal this week' or 'you completed all workouts'.");
            sb.AppendLine();

            sb.AppendLine("=== USER DATA ===");
            sb.AppendLine($"Total workouts logged: {context.TotalWorkouts}");

            if (context.DaysSinceLastWorkout >= 0)
                sb.AppendLine($"Days since last workout: {context.DaysSinceLastWorkout}");
            else
                sb.AppendLine("Last workout: No records.");

            sb.AppendLine($"Workouts this week: {context.WorkoutsThisWeek}");
            var activityNote = context.WorkoutsLast30Days <= 8 ? " ← LOW/MODERATE — do NOT say 'consistent', 'frequently', 'regularly'" : "";
            sb.AppendLine($"Workouts in last 30 days: {context.WorkoutsLast30Days}{activityNote}");

            var uniqueWorkoutNames = context.MuscleGroupFrequency.Keys.ToList();
            if (uniqueWorkoutNames.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("Workout type distribution (last 30 days):");
                foreach (var kv in context.MuscleGroupFrequency.OrderByDescending(kv => kv.Value))
                    sb.AppendLine($"  {kv.Key}: {kv.Value} workouts");
            }

            if (context.RecentWorkouts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Recent workouts — LIST IS ORDERED NEWEST TO OLDEST (first row = MOST RECENT workout):");
                var index = 1;
                foreach (var workout in context.RecentWorkouts)
                {
                    var recency = index == 1 ? "MOST RECENT WORKOUT" : $"{index}th previous workout";
                    sb.AppendLine($"  [{recency}] {workout.WorkoutDate:dd.MM.yyyy} — {workout.WorkoutName}");
                    foreach (var ex in workout.Exercises)
                    {
                        if (ex.MaxWeightKg > 0)
                            sb.AppendLine($"    • {ex.ExerciseName}: {ex.SetCount} sets, session max weight {ex.MaxWeightKg:F1} kg");
                        else
                            sb.AppendLine($"    • {ex.ExerciseName}: {ex.SetCount} sets (no weight recorded)");
                    }
                    index++;
                }
            }

            if (context.WeightTrends.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Weight trends — relay these lines as-is, do not add interpretation:");
                foreach (var trend in context.WeightTrends)
                {
                    var ordered = trend.WeeklyMaxWeights.OrderByDescending(w => w.WeeksAgo).ToList();
                    var oldest = ordered.First();
                    var newest = ordered.Last();
                    var delta = newest.MaxKg - oldest.MaxKg;
                    var deltaStr = delta >= 0 ? $"+{delta:F1}" : $"{delta:F1}";
                    var trendDesc = trend.Trend.ToUpperInvariant() switch
                    {
                        "UP" => "weight is increasing in recent weeks",
                        "DOWN" => "weight is decreasing in recent weeks",
                        "STABLE" => "weight is stable in recent weeks",
                        _ => trend.Trend.ToLowerInvariant()
                    };
                    sb.AppendLine($"  {trend.ExerciseName}: {oldest.MaxKg:F1} kg → {newest.MaxKg:F1} kg ({deltaStr} kg), {trendDesc}");
                }
            }

            if (context.PlateauExercises.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Plateau exercises (max weight unchanged for the last 3 weeks):");
                foreach (var ex in context.PlateauExercises)
                    sb.AppendLine($"  - {ex}");
                sb.AppendLine("For these exercises only, suggest a concrete way forward: deload, rep range change, or exercise variation.");
            }

            sb.AppendLine();
            sb.AppendLine("=== RESPONSE STYLE ===");

            switch ((actionType ?? "free").ToLowerInvariant())
            {
                case "analyze":
                    sb.AppendLine("Analyze the workout data. Express each point with a DIFFERENT sentence structure — do not repeat.");
                    sb.AppendLine("Strengths: only genuine improvements directly supported by the data. If total workouts < 5, do NOT list strengths — say 'not enough data yet'.");
                    sb.AppendLine("Improvement areas: missing muscle groups, low frequency. If the plateau list is empty, do NOT mention it. 3-4 distinct points or paragraphs is enough.");
                    break;
                case "today":
                    if (context.RecentWorkouts.Count > 0)
                    {
                        var lastWorkout = context.RecentWorkouts[0];
                        var lastExNames = string.Join(", ", lastWorkout.Exercises.Select(e => e.ExerciseName));
                        sb.AppendLine($"⛔ AVOID TODAY: The last workout ({lastWorkout.WorkoutDate:dd.MM.yyyy}) included: [{lastExNames}]");
                        sb.AppendLine("Do NOT suggest movements that target the same muscle groups as those exercises. Choose a completely different muscle group.");
                    }
                    sb.AppendLine("Pick EXACTLY 1 muscle group and commit to it. Do not offer alternatives like 'shoulders or back'.");
                    sb.AppendLine("FORMAT: 1 short intro sentence ('I recommend training X today.'), then EXACTLY 2-3 exercises, each on its own line. Do NOT write 4 or more exercises. Do NOT add sentences after the list.");
                    sb.AppendLine("WEIGHT FORMAT: If the exercise has past data in the 'Weight trends' section, write the line as: 'ExerciseName: X sets × Y reps @ Z kg'. If there is no weight data, write: 'ExerciseName: X sets × Y reps'.");
                    sb.AppendLine("ACSM SAFETY RULE: The recommended weight (Z kg) must not exceed 110% of that exercise's most recent session max (ACSM progressive overload ≤10%/week).");
                    sb.AppendLine("Vague openings like 'I can suggest a few exercises' are forbidden — state the muscle group directly.");
                    sb.AppendLine("Use standard fitness terminology for exercise names (Pull-up, Cable Row, T-bar Row). Do not invent translations.");
                    break;
                case "program":
                    sb.AppendLine("Evaluate weekly frequency, muscle group balance, and weight trends.");
                    sb.AppendLine("Each paragraph must cover a DIFFERENT topic: 1st paragraph frequency, 2nd muscle balance, 3rd trend, 4th concrete suggestion. Do not repeat the same idea in two paragraphs.");
                    sb.AppendLine("LAST PARAGRAPH RULE: Do NOT start with 'In conclusion', 'To summarize', 'Therefore'. The last paragraph must not restate the previous ones — it should contain a single concrete data-supported action step.");
                    sb.AppendLine("NO EXERCISE LISTS: Do not add set/rep plans like 'Squat: 3 sets × 10 reps' to a program evaluation.");
                    sb.AppendLine("NO MARKDOWN: Do not use **, *, #, numbered lists (1. 2. 3.), or bullet symbols (- or •). Write in plain paragraphs.");
                    sb.AppendLine("DATA READING RULE: 'Workouts in last 30 days: X' is a 30-day total, not a weekly figure. Do not infer 'you train X times per week'.");
                    sb.AppendLine("TREND RULE: Only comment on trends for exercises that appear in the weight trends section. Do not say 'track your progress' for exercises not listed there.");
                    break;
                case "motivation":
                    sb.AppendLine("GOAL: Motivate the user to train today.");
                    sb.AppendLine("STRUCTURE: Exactly 3 SHORT sentences. Each sentence one idea. Plain text.");
                    sb.AppendLine("TONE: Coach tone — short, direct, energetic.");
                    sb.AppendLine("FIRST WORD: Start with an action word, 'Today', or a concrete number. Do NOT start with 'You', 'Your', 'I', 'Let me'.");
                    sb.AppendLine("DATA: Use 1 concrete number from the data (kg or days).");
                    sb.AppendLine("FORBIDDEN: nostalgic tone, 'remember when', filler phrases.");
                    sb.AppendLine("EXAMPLE: 'Hit the gym — your muscles are ready. You hit 100 kg, now push past it. Move.'");
                    break;
                default:
                    sb.AppendLine("Answer the user's question briefly and naturally. Reference the data when relevant.");
                    break;
            }

            sb.AppendLine();
            sb.AppendLine("=== PRE-SEND CHECKLIST ===");
            sb.AppendLine("Before sending your response, check:");
            sb.AppendLine("1. Is there any non-English word? If so, replace it.");
            sb.AppendLine("2. Do I say I need more data or more information? If so, remove it — work with what is available.");
            sb.AppendLine("3. Did I repeat the same idea in two different sentences? If so, remove one.");

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

        // Prompt kurallarına rağmen modelin ürettiği kalıp ifadeleri çıktı tarafında temizler.
        // Dile göre iki ayrı örüntü seti kullanılır.
        private static string SanitizeOutputPatterns(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var patterns = IsEnglish
                ? new (string Pattern, string Replacement)[]
                {
                    (@"(?i)in conclusion[,.]?\s*", ""),
                    (@"(?i)to summarize[,.]?\s*", ""),
                    (@"(?i)in summary[,.]?\s*", ""),
                    (@"(?i)in light of (this|the above)[,.]?\s*", ""),
                    (@"(?i)therefore[,.]?\s*", ""),
                    // "more data needed" patterns
                    (@"(?i)more (data|information|workouts?) (is |are )?(needed|required)[^\n.]*[.\n]?\s*", ""),
                    (@"(?i)i (would |)need more (data|information)[^\n.]*[.\n]?\s*", ""),
                    // Model explaining its own rules
                    (@"(?i)(the |)plateau list is empty[^\n.]*[.\n]?\s*", ""),
                    (@"(?i)i (will|won'?t) (cover|address|discuss) (this|that)[^\n.]*[.\n]?\s*", ""),
                    // We-form → I/you
                    (@"\bwe can (see|discuss|do|track|review|evaluate|look)\b", "you can $1"),
                    (@"\blet'?s (see|discuss|do|track|review|evaluate|look)\b", ""),
                }
                : new (string Pattern, string Replacement)[]
                {
                    (@"(?i)sonuç olarak[,.]?\s*", ""),
                    (@"(?i)özetle[,.]?\s*", ""),
                    (@"(?i)bu bilgiler ışığında[,.]?\s*", ""),
                    (@"(?i)bu nedenle[,.]?\s*", ""),
                    (@"(?i)düzenli olarak antrenman yap\w*", "antrenman sıklığını artır"),
                    (@"(?i)düzenli olarak çalış\w*", "daha sık çalış"),
                    // "daha fazla veri/antrenman gerekiyor" kalıpları
                    (@"(?i)daha fazla (antrenman |veri |veriye |bilgiye )?(verisi?|ihtiyaç|gerekli)[^\n.]*[.\n]?\s*", ""),
                    (@"(?i)(analiz edilebilmesi|değerlendirilebilmesi) için daha fazla[^\n.]*[.\n]?\s*", ""),
                    // Model kendi kurallarını açıklamasın
                    (@"(?i)(takılma noktası listesi|plato listesi) boş[^\n.]*[.\n]?\s*", ""),
                    (@"(?i)bu konuya (daha derinlemesine |)girilmeyecektir[^\n.]*[.\n]?\s*", ""),
                    // Ağırlık sayılarından kişilik/alışkanlık çıkarımı
                    (@"(?i)farklı zorluk seviyeleriy?le? çalış\w*", "farklı ağırlıklarla çalışmışsın"),
                    (@"(?i)farklı zorluk seviyelerinin? bir göstergesi", ""),
                    // Biz formu → tekil dönüşümü
                    (@"\bkonuşabiliriz\b", "konuşabilirsin"),
                    (@"\bdeğerlendirebiliriz\b", "değerlendirebilirsin"),
                    (@"\bçalışabiliriz\b", "çalışabilirsin"),
                    (@"\bbakabiliriz\b", "bakabilirsin"),
                    (@"\byapabiliriz\b", "yapabilirsin"),
                    (@"\bsöyleyebiliriz\b", "söyleyebilirim"),
                    (@"\bgörebiliriz\b", "görebilirsin"),
                    (@"\bincleyebiliriz\b", "inceleyebilirsin"),
                };

            foreach (var (pattern, replacement) in patterns)
                text = System.Text.RegularExpressions.Regex.Replace(text, pattern, replacement);

            return text.Trim();
        }

        // Türkçe hedefte: bilinen yabancı bağlaç/dolgu kelimelerini güvenli kelime sınırı
        // kontrollü şekilde değiştirir (fitness terimleri tam kelime eşleşmesi olmadığı için etkilenmez).
        // İngilizce hedefte: hedef dil zaten İngilizce olduğu için no-op.
        private static string SanitizeForeignWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (IsEnglish) return text;

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
                (@"(?<!\w)siguientes\w*", "aşağıdaki"),
                (@"(?<!\w)siguiente\w*", "aşağıdaki"),
                (@"(?<!\w)seguinte\w*", "aşağıdaki"),
                (@"(?<!\w)realizado\w*", "yapıldı"),
                (@"(?<!\w)realiz[ae]\w*", "yapıldı"),
                (@"(?<!\w)necessário\w*", "gerekli"),
                (@"(?<!\w)necessario\w*", "gerekli"),
                (@"(?<!\w)nécessaire\w*", "gerekli"),
                (@"(?<!\w)también\w*", "de/da"),
                (@"(?<!\w)tambien\w*", "de/da"),
                (@"(?<!\w)pero\b", "ancak"),
                (@"(?<!\w)es decir\b", "yani"),
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
                // İngilizce dolgu kelimeleri — Türkçe ek alabilir (already→alreadyi gibi), bu yüzden \w* ile yakalanıyor
                (@"(?<!\w)already\w*", "zaten"),
                (@"(?<!\w)mostly\w*", "çoğunlukla"),
                (@"(?<!\w)however\w*", "ancak"),
                (@"(?<!\w)therefore\w*", "bu nedenle"),
                (@"(?<!\w)overall\w*", "genel olarak"),
                (@"(?<!\w)regularly\w*", "düzenli olarak"),
                (@"(?<!\w)necessary\w*", "gerekli"),
                (@"(?<!\w)important(e|es)?\w*", "önemli"),
                (@"(?<!\w)essential\w*", "gerekli"),
                (@"(?<!\w)slightly\w*", "biraz"),
                (@"(?<!\w)actually\w*", "aslında"),
                (@"(?<!\w)basically\w*", "temelde"),
                (@"(?<!\w)specifically\w*", "özellikle"),
                (@"(?<!\w)currently\w*", "şu an"),
                (@"(?<!\w)potentially\w*", "potansiyel olarak"),
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
