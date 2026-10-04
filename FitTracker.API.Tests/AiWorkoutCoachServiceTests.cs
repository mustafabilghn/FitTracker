using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// FitBot'un Groq HttpClient -> Semantic Kernel geçişinin (Phase 1) regresyon testleri.
/// Servis, production'daki aynı DI kaydıyla (<see cref="GroqChatCompletion.AddGroqSemanticKernel"/>) ve gerçek
/// Semantic Kernel OpenAI-uyumlu connector'üyle çalışır; yalnızca ağ katmanı sahte bir HttpMessageHandler ile
/// değiştirilir. Böylece gerçek Groq API'sine ve API key'e ihtiyaç duyulmaz, ama teldeki istek biçimi de doğrulanır.
/// </summary>
public class AiWorkoutCoachServiceTests
{
    private const string UserId = "user-1";

    // ───────────────────────── Chat ─────────────────────────

    [Fact]
    public async Task Chat_Success_ReturnsReply_AndSendsExpectedGroqRequest()
    {
        using var h = Harness.Create(model: "llama-test-model");
        h.Handler.EnqueueReply("Bugün sırt çalış.");

        var response = await h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "Bugün ne yapayım?", ActionType = "free" });

        Assert.Equal("Bugün sırt çalış.", response.Reply);
        Assert.False(response.GuardrailTriggered);
        Assert.Empty(response.InterceptedProgressions);

        var request = Assert.Single(h.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", request.Uri.ToString());
        Assert.Equal("Bearer test-key", request.Authorization);

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("llama-test-model", root.GetProperty("model").GetString());
        Assert.Equal(0.3, root.GetProperty("temperature").GetDouble(), 3);
        Assert.False(root.TryGetProperty("response_format", out _));
        Assert.False(root.TryGetProperty("tools", out _)); // Phase 1: function calling yok

        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Bugün ne yapayım?", MessageText(messages[1]));
        // Eski HttpClient implementasyonu gibi, system prompt context verisini içerir.
        Assert.Contains("FitBot", MessageText(messages[0]));
        Assert.Contains("Bench Press", MessageText(messages[0]));
    }

    [Fact]
    public async Task Chat_MessagesAreSentAsPlainStrings_NotContentPartArrays()
    {
        // Groq'un OpenAI-uyumlu endpoint'i system/assistant mesajlarında düz string content bekler;
        // eski manuel JSON de düz string gönderiyordu. Connector'ün bunu bozmadığından emin ol.
        using var h = Harness.Create();
        h.Handler.EnqueueReply("ok");

        await h.Service.ChatAsync(UserId, new FitBotChatRequestDto
        {
            Message = "Selam",
            ConversationHistory = new() { new() { Role = "user", Content = "Merhaba" }, new() { Role = "assistant", Content = "Selam!" } }
        });

        using var body = JsonDocument.Parse(h.Handler.Requests.Single().Body);
        foreach (var message in body.RootElement.GetProperty("messages").EnumerateArray())
            Assert.Equal(JsonValueKind.String, message.GetProperty("content").ValueKind);
    }

    [Fact]
    public async Task Chat_ConversationHistory_KeepsLastSix_MapsRoles_AndKeepsOrder()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueReply("ok");

        var history = new List<FitBotConversationMessageDto>
        {
            new() { Role = "user", Content = "m1" },
            new() { Role = "assistant", Content = "m2" },
            new() { Role = "user", Content = "m3" },
            new() { Role = "assistant", Content = "m4" },
            new() { Role = "USER", Content = "m5" },       // büyük/küçük harf duyarsız
            new() { Role = "bot", Content = "m6" },        // bilinmeyen rol -> assistant
            new() { Role = "user", Content = "m7" },
            new() { Role = "assistant", Content = "m8" },
        };

        await h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "son soru", ConversationHistory = history });

        using var body = JsonDocument.Parse(h.Handler.Requests.Single().Body);
        var messages = body.RootElement.GetProperty("messages").EnumerateArray()
            .Select(m => (Role: m.GetProperty("role").GetString(), Text: MessageText(m)))
            .ToList();

        // system + son 6 geçmiş mesajı (m3..m8) + yeni kullanıcı mesajı
        Assert.Equal(8, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            new[] { ("user", "m3"), ("assistant", "m4"), ("user", "m5"), ("assistant", "m6"), ("user", "m7"), ("assistant", "m8"), ("user", "son soru") },
            messages.Skip(1).Select(m => (m.Role!, m.Text)).ToArray());
    }

    [Fact]
    public async Task Chat_Motivation_UsesHigherTemperature_AndTruncatesToFourSentences()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueReply("Bir! İki! Üç! Dört! Beş! Altı!");

        var response = await h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "motive et", ActionType = "motivation" });

        using var body = JsonDocument.Parse(h.Handler.Requests.Single().Body);
        Assert.Equal(0.6, body.RootElement.GetProperty("temperature").GetDouble(), 3);
        Assert.Equal("Bir! İki! Üç! Dört!", response.Reply);
    }

    [Fact]
    public async Task Chat_SanitizationAndGuardrailPipeline_RunsOnModelReply()
    {
        using var h = Harness.Create();
        // Bench Press baseline 100 kg -> güvenli sınır 110 kg. 125 kg ACSM guardrail tarafından kısılmalı,
        // "Sonuç olarak" kalıbı ve yabancı kelime (however) sanitization tarafından temizlenmeli.
        h.Handler.EnqueueReply(
            "Bugün göğüs çalış, however dikkatli ol.\n" +
            "Bench Press: 3 set × 8 tekrar @ 125 kg\n" +
            "Sonuç olarak iyi gidiyorsun.");

        var response = await h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "bugün", ActionType = "today" });

        Assert.True(response.GuardrailTriggered);
        var intercepted = Assert.Single(response.InterceptedProgressions);
        Assert.Contains("Bench Press", intercepted);
        Assert.Contains("@ 110.0 kg", response.Reply);
        Assert.DoesNotContain("125", response.Reply);
        Assert.DoesNotContain("Sonuç olarak", response.Reply);
        Assert.DoesNotContain("however", response.Reply);
        Assert.Contains("ancak", response.Reply);
    }

    [Fact]
    public async Task Chat_ReturnsPlateauAlertsFromContext()
    {
        using var h = Harness.Create();
        h.Analysis.Context.PlateauExercises.Add("Squat");
        h.Handler.EnqueueReply("ok");

        var response = await h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "x" });

        Assert.Equal(new[] { "Squat" }, response.PlateauAlerts);
    }

    [Fact]
    public async Task Chat_ContextIsCachedBetweenCalls_AndTelemetryHeadersAreWritten()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueReply("bir");
        h.Handler.EnqueueReply("iki");

        await h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "a" });
        Assert.Equal("miss", h.HttpContext.Response.Headers["X-FitBot-Context-Cache"].ToString());
        Assert.True(long.TryParse(h.HttpContext.Response.Headers["X-FitBot-Context-Ms"], out _));
        Assert.True(long.TryParse(h.HttpContext.Response.Headers["X-FitBot-Groq-Ms"], out _));

        await h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "b" });
        Assert.Equal("hit", h.HttpContext.Response.Headers["X-FitBot-Context-Cache"].ToString());
        Assert.Equal(1, h.Analysis.ContextCalls);
    }

    [Fact]
    public async Task Chat_TooManyRequests_ReturnsFriendlyReply_WithoutRetrying_Turkish()
    {
        using var h = Harness.Create(culture: "tr-TR");
        h.Handler.EnqueueStatus(HttpStatusCode.TooManyRequests, """{"error":{"message":"Rate limit reached","type":"tokens","code":"rate_limit_exceeded"}}""");

        var response = await h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "x" });

        Assert.Equal("Şu an çok fazla istek var, biraz bekleyip tekrar dene.", response.Reply);
        Assert.Empty(response.PlateauAlerts);
        Assert.False(response.GuardrailTriggered);
        // SDK'nın otomatik retry'ı kapalı olmalı: eski davranışta 429 hemen kullanıcıya dönüyordu.
        Assert.Single(h.Handler.Requests);
        // 429 yolunda da timing telemetry yazılır (önceki davranış).
        Assert.True(h.HttpContext.Response.Headers.ContainsKey("X-FitBot-Groq-Ms"));
    }

    [Fact]
    public async Task Chat_TooManyRequests_ReturnsFriendlyReply_English()
    {
        using var h = Harness.Create(culture: "en-US");
        h.Handler.EnqueueStatus(HttpStatusCode.TooManyRequests, "{}");

        var response = await h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "x" });

        Assert.Equal("Too many requests right now — wait a moment and try again.", response.Reply);
        Assert.Single(h.Handler.Requests);
    }

    [Fact]
    public async Task Chat_OtherHttpError_ThrowsInvalidOperationWithStatusAndBody()
    {
        using var h = Harness.Create(culture: "tr-TR");
        h.Handler.EnqueueStatus(HttpStatusCode.InternalServerError, """{"error":{"message":"boom"}}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "x" }));

        Assert.StartsWith("Groq API hatası InternalServerError:", ex.Message);
        Assert.Contains("boom", ex.Message);
        Assert.Single(h.Handler.Requests); // 5xx'te de otomatik retry yok
    }

    [Fact]
    public async Task Chat_OtherHttpError_English_ThrowsInvalidOperationWithStatusAndBody()
    {
        using var h = Harness.Create(culture: "en-US");
        h.Handler.EnqueueStatus(HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid API Key"}}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "x" }));

        Assert.StartsWith("Groq API error Unauthorized:", ex.Message);
        Assert.Contains("Invalid API Key", ex.Message);
    }

    [Fact]
    public async Task Chat_MissingApiKey_Throws_BeforeAnyHttpCall()
    {
        using var h = Harness.Create(apiKey: null, culture: "tr-TR");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "x" }));

        Assert.Equal("Groq yapılandırması eksik.", ex.Message);
        Assert.Empty(h.Handler.Requests);
    }

    [Fact]
    public async Task Chat_MissingApiKey_English_Throws()
    {
        using var h = Harness.Create(apiKey: " ", culture: "en-US");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "x" }));

        Assert.Equal("Groq configuration is missing.", ex.Message);
    }

    [Fact]
    public async Task Chat_BlankUserId_ReturnsIdentityMessage_WithoutCallingGroq()
    {
        using var h = Harness.Create(culture: "tr-TR");

        var response = await h.Service.ChatAsync(" ", new FitBotChatRequestDto { Message = "x" });

        Assert.Equal("Kullanıcı bilgisi alınamadı.", response.Reply);
        Assert.Empty(h.Handler.Requests);
    }

    [Fact]
    public async Task Chat_EnglishCulture_UsesEnglishSystemPrompt_TurkishCulture_UsesTurkish()
    {
        using (var en = Harness.Create(culture: "en-US"))
        {
            en.Handler.EnqueueReply("ok");
            await en.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "x" });
            Assert.Contains("LANGUAGE RULE", SystemPrompt(en.Handler.Requests.Single()));
        }

        using (var tr = Harness.Create(culture: "tr-TR"))
        {
            tr.Handler.EnqueueReply("ok");
            await tr.Service.ChatAsync(UserId, new FitBotChatRequestDto { Message = "x" });
            Assert.Contains("DİL KURALI", SystemPrompt(tr.Handler.Requests.Single()));
        }
    }

    // ───────────────────────── Insights ─────────────────────────

    [Fact]
    public async Task Insights_Success_ParsesModelJson_AndRequestsJsonObjectFormat()
    {
        using var h = Harness.Create(culture: "tr-TR");
        h.Handler.EnqueueReply("""
            {"summary":"Özet","strengths":["Güçlü 1"],"improvements":["Geliştir 1","Geliştir 2"],"nextWorkoutSuggestion":"Sırt çalış"}
            """);

        var insight = await h.Service.GetInsightsAsync(UserId);

        Assert.Equal("Özet", insight.Summary);
        Assert.Equal(new[] { "Güçlü 1" }, insight.Strengths);
        Assert.Equal(new[] { "Geliştir 1", "Geliştir 2" }, insight.Improvements);
        Assert.Equal("Sırt çalış", insight.NextWorkoutSuggestion);

        var request = Assert.Single(h.Handler.Requests);
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", request.Uri.ToString());
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal(0.2, root.GetProperty("temperature").GetDouble(), 3);
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(new[] { "system", "user" }, messages.Select(m => m.GetProperty("role").GetString()).ToArray());
        Assert.Contains("summary, strengths, improvements, nextWorkoutSuggestion", MessageText(messages[0]));
        Assert.Contains("\"TotalWorkouts\":10", MessageText(messages[1])); // analiz JSON'u user mesajında
    }

    [Fact]
    public async Task Insights_NoWorkouts_ReturnsStaticResponse_WithoutCallingGroq()
    {
        using var h = Harness.Create(culture: "tr-TR");
        h.Analysis.Analysis.TotalWorkouts = 0;

        var insight = await h.Service.GetInsightsAsync(UserId);

        Assert.Equal("Henüz analiz yapacak kadar antrenman verisi yok.", insight.Summary);
        Assert.Empty(h.Handler.Requests);
    }

    [Fact]
    public async Task Insights_EmptyModelContent_ReturnsEmptyDto()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueReply("");

        var insight = await h.Service.GetInsightsAsync(UserId);

        Assert.Equal(string.Empty, insight.Summary);
        Assert.Empty(insight.Strengths);
        Assert.Empty(insight.Improvements);
    }

    [Fact]
    public async Task Insights_BlankUserId_ReturnsEmptyDto()
    {
        using var h = Harness.Create();

        var insight = await h.Service.GetInsightsAsync("");

        Assert.Equal(string.Empty, insight.Summary);
        Assert.Empty(h.Handler.Requests);
    }

    [Fact]
    public async Task Insights_MissingApiKey_Throws_BeforeAnyHttpCall()
    {
        using var h = Harness.Create(apiKey: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetInsightsAsync(UserId));

        Assert.Equal("Groq configuration is missing. Please set Groq:ApiKey and Groq:Model.", ex.Message);
        Assert.Empty(h.Handler.Requests);
    }

    [Fact]
    public async Task Insights_HttpError_ThrowsInvalidOperationWithStatusAndBody()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueStatus(HttpStatusCode.TooManyRequests, """{"error":{"message":"Rate limit reached"}}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.GetInsightsAsync(UserId));

        // Insights yolunda 429 özel ele alınmaz (önceki davranış): genel hata fırlatılır.
        Assert.StartsWith("Groq API request failed with status code TooManyRequests:", ex.Message);
        Assert.Contains("Rate limit reached", ex.Message);
        Assert.Single(h.Handler.Requests);
    }

    // ───────────────────────── DI / lifecycle ─────────────────────────

    [Fact]
    public void Di_MissingGroqConfig_DoesNotFailAtStartup_AndKernelIsTransientChatServiceScoped()
    {
        using var h = Harness.Create(apiKey: null);

        // Yapılandırma eksik olsa bile Kernel çözümlenebilmeli (hata, eskisi gibi çağrı anında oluşur).
        using var scope = h.Provider.CreateScope();
        var kernelA = scope.ServiceProvider.GetRequiredService<Kernel>();
        var kernelB = scope.ServiceProvider.GetRequiredService<Kernel>();
        Assert.NotSame(kernelA, kernelB); // transient: ileride request-scoped plugin'ler paylaşılmaz

        Harness.UseKey(h.Provider, "k"); // anahtar sonradan gelirse chat servisi o scope'ta oluşturulabilir
    }

    [Fact]
    public void Di_ChatCompletionService_IsScopedAndSharedByKernelsInSameScope()
    {
        using var h = Harness.Create();

        using var scope1 = h.Provider.CreateScope();
        using var scope2 = h.Provider.CreateScope();
        var a = scope1.ServiceProvider.GetRequiredService<Kernel>().GetRequiredService<IChatCompletionService>();
        var b = scope1.ServiceProvider.GetRequiredService<Kernel>().GetRequiredService<IChatCompletionService>();
        var c = scope2.ServiceProvider.GetRequiredService<Kernel>().GetRequiredService<IChatCompletionService>();

        Assert.Same(a, b);
        Assert.NotSame(a, c);
    }

    // ───────────────────────── Harness ─────────────────────────

    private static string MessageText(JsonElement message)
    {
        var content = message.GetProperty("content");
        return content.ValueKind == JsonValueKind.String
            ? content.GetString()!
            : string.Concat(content.EnumerateArray().Select(p => p.GetProperty("text").GetString()));
    }

    private static string SystemPrompt(CapturedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body);
        return MessageText(body.RootElement.GetProperty("messages")[0]);
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        public List<CapturedRequest> Requests { get; } = new();

        public void EnqueueReply(string content) => _responses.Enqueue(() => Json(HttpStatusCode.OK, GroqCompletion(content)));
        public void EnqueueStatus(HttpStatusCode status, string body) => _responses.Enqueue(() => Json(status, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));

            if (_responses.Count == 0)
                throw new InvalidOperationException("Test beklenmeyen bir Groq çağrısı yaptı (kuyrukta cevap yok).");

            return _responses.Dequeue()();
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        // Gerçek Groq cevabına benzer: x_groq, usage.queue_time gibi OpenAI şemasında olmayan alanlar dahil.
        private static string GroqCompletion(string content) => JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            @object = "chat.completion",
            created = 1700000000,
            model = "llama-test-model",
            choices = new[]
            {
                new { index = 0, message = new { role = "assistant", content }, logprobs = (object?)null, finish_reason = "stop" }
            },
            usage = new { queue_time = 0.01, prompt_tokens = 10, prompt_time = 0.001, completion_tokens = 5, completion_time = 0.01, total_tokens = 15, total_time = 0.011 },
            system_fingerprint = "fp_test",
            x_groq = new { id = "req_test" }
        });
    }

    private sealed class FakeWorkoutAnalysisService : IWorkoutAnalysisService
    {
        public int ContextCalls { get; private set; }
        public WorkoutAnalysisDto Analysis { get; } = new() { TotalWorkouts = 10, WorkoutsLast30Days = 8 };

        // Bench Press baseline: 100 kg -> ACSM güvenli sınırı 110 kg
        public FitBotContextDto Context { get; } = new()
        {
            TotalWorkouts = 10,
            DaysSinceLastWorkout = 2,
            WorkoutsThisWeek = 2,
            WorkoutsLast30Days = 8,
            WeightTrends = new()
            {
                new()
                {
                    ExerciseName = "Bench Press",
                    Trend = "UP",
                    WeeklyMaxWeights = new() { new() { WeeksAgo = 0, MaxKg = 100 }, new() { WeeksAgo = 2, MaxKg = 95 } }
                }
            },
            RecentWorkouts = new()
            {
                new()
                {
                    WorkoutName = "Göğüs",
                    WorkoutDate = new DateTime(2026, 5, 1),
                    Exercises = new() { new() { ExerciseName = "Bench Press", MaxWeightKg = 100, SetCount = 3, Reps = "8" } }
                }
            }
        };

        public Task<WorkoutAnalysisDto> GetAnalysisAsync(string userId) => Task.FromResult(Analysis);

        public Task<FitBotContextDto> GetFitBotContextAsync(string userId)
        {
            ContextCalls++;
            return Task.FromResult(Context);
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly IServiceScope _scope;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public ServiceProvider Provider { get; }
        public StubHandler Handler { get; } = new();
        public FakeWorkoutAnalysisService Analysis { get; } = new();
        public DefaultHttpContext HttpContext { get; } = new();
        public AiWorkoutCoachService Service { get; }

        private Harness(string? apiKey, string? model, string culture)
        {
            // IsEnglish, CultureInfo.CurrentUICulture'dan okunur (Program.cs'te RequestLocalization yazar).
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            var settings = new Dictionary<string, string?>();
            if (apiKey != null) settings["Groq:ApiKey"] = apiKey;
            if (model != null) settings["Groq:Model"] = model;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddGroqSemanticKernel(); // production ile aynı kayıt
            services.AddHttpClient(GroqChatCompletion.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Handler);

            Provider = services.BuildServiceProvider();
            _scope = Provider.CreateScope();

            Service = new AiWorkoutCoachService(
                _scope.ServiceProvider.GetRequiredService<Kernel>(),
                Analysis,
                new AcsmGuardrailService(),
                _cache,
                new HttpContextAccessor { HttpContext = HttpContext },
                NullLogger<AiWorkoutCoachService>.Instance,
                configuration);
        }

        public static Harness Create(string? apiKey = "test-key", string? model = "llama-test-model", string culture = "tr-TR")
            => new(apiKey, model, culture);

        // Anahtarı sonradan veren yapılandırmanın aynı DI kaydıyla çalıştığını doğrular.
        public static void UseKey(ServiceProvider provider, string key)
        {
            var configuration = (IConfigurationRoot)provider.GetRequiredService<IConfiguration>();
            configuration["Groq:ApiKey"] = key;
            using var scope = provider.CreateScope();
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<Kernel>().GetRequiredService<IChatCompletionService>());
        }

        public void Dispose()
        {
            _scope.Dispose();
            Provider.Dispose();
            _cache.Dispose();
        }
    }
}
