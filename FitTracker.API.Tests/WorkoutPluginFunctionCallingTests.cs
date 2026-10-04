using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Plugins;
using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// FitBot function calling (Phase 2) testleri. Production ile aynı DI kaydı kullanılır
/// (AddGroqSemanticKernel + AddFitBotWorkoutPlugin + scoped AiWorkoutCoachService, scope doğrulaması açık);
/// yalnızca LLM transport'u sahte bir handler ile değiştirilir. Böylece gerçek Groq API'sine ve API key'e ihtiyaç
/// yoktur, ama "model → function call → plugin → tool result → final response" zinciri gerçek Semantic Kernel
/// connector'ü üzerinden uçtan uca çalışır.
/// </summary>
public class WorkoutPluginFunctionCallingTests
{
    private const string UserA = "user-A";
    private const string UserB = "user-B";

    // ───────────────────── A. Function registration ─────────────────────

    [Fact]
    public void A_Plugin_IsRegisteredOnKernel_WithExpectedFunctions_AndNoUserIdParameters()
    {
        using var h = Harness.Create();
        using var scope = h.Provider.CreateScope();
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        Assert.True(kernel.Plugins.TryGetPlugin(WorkoutPlugin.PluginName, out var plugin));
        Assert.Equal(
            new[] { "GetPlateauExercises", "GetRecentWorkouts", "GetWeightTrends" },
            plugin!.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());

        foreach (var function in plugin)
        {
            Assert.False(string.IsNullOrWhiteSpace(function.Description));
            // Güvenlik sözleşmesi: hiçbir fonksiyon kimlik/gizli bilgi argümanı almaz.
            Assert.DoesNotContain(function.Metadata.Parameters,
                p => p.Name.Contains("user", StringComparison.OrdinalIgnoreCase)
                  || p.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
                  || p.Name.Contains("connection", StringComparison.OrdinalIgnoreCase));
        }

        var trendParams = plugin["GetWeightTrends"].Metadata.Parameters;
        Assert.Equal(new[] { "exerciseName" }, trendParams.Select(p => p.Name).ToArray());
        Assert.False(trendParams[0].IsRequired);
    }

    // ───────────────────── B. Function execution ─────────────────────

    [Fact]
    public async Task B_GetWeightTrends_ReturnsCurrentUsersData()
    {
        using var h = Harness.Create();
        using var scope = h.Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(UserA);
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        var all = (await kernel.InvokeAsync(WorkoutPlugin.PluginName, "GetWeightTrends")).GetValue<List<ExerciseWeightTrendDto>>()!;
        var trend = Assert.Single(all);
        Assert.Equal("Bench Press", trend.ExerciseName);
        Assert.Equal(100, trend.WeeklyMaxWeights.Single(w => w.WeeksAgo == 0).MaxKg);

        var filtered = (await kernel.InvokeAsync(WorkoutPlugin.PluginName, "GetWeightTrends",
            new KernelArguments { ["exerciseName"] = "bench" })).GetValue<List<ExerciseWeightTrendDto>>()!;
        Assert.Single(filtered);

        var none = (await kernel.InvokeAsync(WorkoutPlugin.PluginName, "GetWeightTrends",
            new KernelArguments { ["exerciseName"] = "squat" })).GetValue<List<ExerciseWeightTrendDto>>()!;
        Assert.Empty(none);
    }

    [Fact]
    public async Task B_PlateauAndRecentWorkouts_ReuseTheSameContextCalculation_AndClampCount()
    {
        using var h = Harness.Create();
        using var scope = h.Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(UserA);
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        var plateau = (await kernel.InvokeAsync(WorkoutPlugin.PluginName, "GetPlateauExercises")).GetValue<List<string>>()!;
        Assert.Equal(h.Analysis.Contexts[UserA].PlateauExercises, plateau); // aynı kaynak: GetFitBotContextAsync

        async Task<int> Recent(int? count)
        {
            var args = count is null ? new KernelArguments() : new KernelArguments { ["count"] = count };
            return (await kernel.InvokeAsync(WorkoutPlugin.PluginName, "GetRecentWorkouts", args))
                .GetValue<List<WorkoutContextEntryDto>>()!.Count;
        }

        Assert.Equal(5, await Recent(null)); // varsayılan 5
        Assert.Equal(10, await Recent(100)); // üst sınır 10
        Assert.Equal(1, await Recent(0));    // alt sınır 1
    }

    // ───────────────────── C. Current user isolation ─────────────────────

    [Fact]
    public async Task C_EachScope_OnlySeesItsOwnUsersData()
    {
        using var h = Harness.Create();
        using var scopeA = h.Provider.CreateScope();
        using var scopeB = h.Provider.CreateScope();
        scopeA.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(UserA);
        scopeB.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(UserB);

        var a = (await scopeA.ServiceProvider.GetRequiredService<Kernel>().InvokeAsync(WorkoutPlugin.PluginName, "GetWeightTrends"))
            .GetValue<List<ExerciseWeightTrendDto>>()!;
        var b = (await scopeB.ServiceProvider.GetRequiredService<Kernel>().InvokeAsync(WorkoutPlugin.PluginName, "GetWeightTrends"))
            .GetValue<List<ExerciseWeightTrendDto>>()!;

        Assert.Equal(new[] { "Bench Press" }, a.Select(t => t.ExerciseName).ToArray());
        Assert.Equal(new[] { "B Only Exercise" }, b.Select(t => t.ExerciseName).ToArray());
        Assert.Equal(new[] { UserA, UserB }, h.Analysis.RequestedUserIds.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task C_ModelSuppliedUserIdArgument_IsIgnored()
    {
        using var h = Harness.Create();
        using var scope = h.Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(UserA);
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        var result = (await kernel.InvokeAsync(WorkoutPlugin.PluginName, "GetWeightTrends",
            new KernelArguments { ["userId"] = UserB, ["exerciseName"] = "Bench Press" })).GetValue<List<ExerciseWeightTrendDto>>()!;

        Assert.Equal("Bench Press", Assert.Single(result).ExerciseName);
        Assert.All(h.Analysis.RequestedUserIds, id => Assert.Equal(UserA, id));
    }

    [Fact]
    public async Task C_NoCurrentUser_FailsClosed_WithoutReadingAnyData()
    {
        using var h = Harness.Create();
        using var scope = h.Provider.CreateScope(); // SetUser çağrılmadı
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        var result = await kernel.InvokeAsync(WorkoutPlugin.PluginName, "GetWeightTrends");

        Assert.Contains("could not be identified", JsonSerializer.Serialize(result.GetValue<object>()));
        Assert.Empty(h.Analysis.RequestedUserIds);
    }

    [Fact]
    public void C_CurrentUser_CannotBeSwitchedWithinAScope()
    {
        var context = new CurrentUserContext();
        Assert.Null(context.UserId);

        context.SetUser(UserA);
        context.SetUser(UserA); // aynı kullanıcı: idempotent
        Assert.Equal(UserA, context.UserId);

        Assert.Throws<InvalidOperationException>(() => context.SetUser(UserB));
        Assert.Equal(UserA, context.UserId);
        Assert.Throws<ArgumentException>(() => new CurrentUserContext().SetUser(" "));
    }

    // ───────────────────── D. Real function-call flow ─────────────────────

    [Fact]
    public async Task D_ModelCallsGetWeightTrends_PluginRuns_ResultGoesBack_FinalAnswerReturned()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueToolCall("call_1", "Workout-GetWeightTrends", """{"exerciseName":"Bench Press"}""");
        h.Handler.EnqueueReply("Bench Press'te 95 kg'dan 100 kg'a çıktın.");

        var response = await h.ChatAsync(UserA, "Bench Press'te son haftalarda nasıl ilerledim?");

        // 1) Model → function call → plugin → tool result → model → final response
        Assert.Equal(2, h.Handler.Requests.Count);
        Assert.Equal("Bench Press'te 95 kg'dan 100 kg'a çıktın.", response.Reply);

        // 2) İlk istek: tool'lar sunuldu, userId parametresi yok, sıralı çağrı (parallel_tool_calls gönderilmedi).
        var first = Json(h.Handler.Requests[0]);
        var toolNames = first.GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("function").GetProperty("name").GetString()).ToList();
        Assert.Equal(
            new[] { "Workout-GetPlateauExercises", "Workout-GetRecentWorkouts", "Workout-GetWeightTrends" },
            toolNames.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal("auto", first.GetProperty("tool_choice").GetString());
        Assert.False(first.TryGetProperty("parallel_tool_calls", out _));
        Assert.DoesNotContain("userid", h.Handler.Requests[0].Body, StringComparison.OrdinalIgnoreCase);

        // 3) İkinci istek: assistant tool_call + tool sonucu (current user'ın verisi) geri gönderildi.
        var second = Json(h.Handler.Requests[1]);
        var messages = second.GetProperty("messages").EnumerateArray().ToList();
        var assistant = messages[^2];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        var call = assistant.GetProperty("tool_calls")[0];
        Assert.Equal("call_1", call.GetProperty("id").GetString());
        Assert.Equal("Workout-GetWeightTrends", call.GetProperty("function").GetProperty("name").GetString());

        var tool = messages[^1];
        Assert.Equal("tool", tool.GetProperty("role").GetString());
        Assert.Equal("call_1", tool.GetProperty("tool_call_id").GetString());
        var toolContent = MessageText(tool);
        Assert.Contains("Bench Press", toolContent);
        Assert.Contains("100", toolContent);
        Assert.DoesNotContain("B Only Exercise", toolContent);

        // 4) Mevcut context injection hâlâ var: system prompt'ta kullanıcı verisi duruyor.
        Assert.Contains("Bench Press", MessageText(messages[0]));
        Assert.Contains("KULLANICI VERİSİ", MessageText(messages[0]));

        // 5) Tool, kimliği doğrulanmış kullanıcının verisini okudu (1 context injection + 1 tool çağrısı).
        Assert.Equal(new[] { UserA, UserA }, h.Analysis.RequestedUserIds.ToArray());
    }

    [Fact]
    public async Task D_ModelTriesToPassAnotherUsersId_StillOnlyCurrentUsersDataIsReturned()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueToolCall("call_x", "Workout-GetWeightTrends", $$"""{"userId":"{{UserB}}","exerciseName":"Bench Press"}""");
        h.Handler.EnqueueReply("tamam");

        await h.ChatAsync(UserA, "bench nasıl?");

        var toolContent = MessageText(Json(h.Handler.Requests[1]).GetProperty("messages").EnumerateArray().Last());
        Assert.Contains("Bench Press", toolContent);
        Assert.DoesNotContain("B Only Exercise", toolContent);
        Assert.DoesNotContain("777", toolContent); // B'nin ağırlıkları
        Assert.All(h.Analysis.RequestedUserIds, id => Assert.Equal(UserA, id));
    }

    [Fact]
    public async Task D_SequentialToolCalls_AreExecutedOneAfterAnother()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueToolCall("c1", "Workout-GetPlateauExercises", "{}");
        h.Handler.EnqueueToolCall("c2", "Workout-GetRecentWorkouts", """{"count":2}""");
        h.Handler.EnqueueReply("bitti");

        var response = await h.ChatAsync(UserA, "plato ve son antrenmanlarım?");

        Assert.Equal("bitti", response.Reply);
        Assert.Equal(3, h.Handler.Requests.Count);
        var last = Json(h.Handler.Requests[2]).GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(new[] { "tool", "tool" }, last.Where(m => m.GetProperty("role").GetString() == "tool")
            .Select(m => m.GetProperty("role").GetString()).ToArray());
    }

    [Fact]
    public async Task D_RunawayToolCalls_AreBounded_ToProtectTokenLimits()
    {
        using var h = Harness.Create();
        // Sürekli tool çağıran (kötü davranan) bir model: SK varsayılanı bunu sınırlamıyordu (13 istek atıyordu).
        for (var i = 0; i < 12; i++)
            h.Handler.EnqueueToolCall($"c{i}", "Workout-GetPlateauExercises", "{}");
        h.Handler.EnqueueReply("FINAL");

        var response = await h.ChatAsync(UserA, "x"); // çökmemeli

        var max = ToolRoundLimitFilter.MaxToolRounds;
        Assert.Equal(max + 1, h.Handler.Requests.Count); // max tool turu + tool çağrısı yasak son tur
        for (var i = 0; i < max; i++)
            Assert.Equal("auto", Json(h.Handler.Requests[i]).GetProperty("tool_choice").GetString());
        // Limit aşıldıktan sonra tool çağrısı yasaklanır: model metin cevap vermek zorunda.
        Assert.Equal("none", Json(h.Handler.Requests[max]).GetProperty("tool_choice").GetString());
        Assert.NotNull(response);
        // Her tur plugin'i gerçekten çalıştırdı (1 context injection + max tool okuması).
        Assert.Equal(1 + max, h.Analysis.RequestedUserIds.Count);
    }

    [Fact]
    public async Task D_ToolRoundLimit_DoesNotAffectNormalFlows_AndFinalReplyAfterLimitIsReturned()
    {
        using var h = Harness.Create();
        var max = ToolRoundLimitFilter.MaxToolRounds;
        // Model tam limit kadar tool çağırır, sonra (tool_choice: none iken) gerçek bir metin cevap verir.
        for (var i = 0; i < max; i++)
            h.Handler.EnqueueToolCall($"c{i}", "Workout-GetPlateauExercises", "{}");
        h.Handler.EnqueueReply("Limit sonrası nihai cevap.");

        var response = await h.ChatAsync(UserA, "x");

        Assert.Equal("Limit sonrası nihai cevap.", response.Reply);
        Assert.Equal(max + 1, h.Handler.Requests.Count);
        // Son istek, önceki tüm tool sonuçlarını içerir ve tool çağrısına izin vermez.
        var last = Json(h.Handler.Requests[max]);
        Assert.Equal("none", last.GetProperty("tool_choice").GetString());
        Assert.Equal(max, last.GetProperty("messages").EnumerateArray().Count(m => m.GetProperty("role").GetString() == "tool"));
    }

    // ───────────────────── E/H. Only free-form chat gets tools; existing behavior kept ─────────────────────

    [Theory]
    [InlineData("analyze")]
    [InlineData("today")]
    [InlineData("program")]
    [InlineData("motivation")]
    [InlineData("ANALYZE")]
    public async Task E_PresetActions_DoNotGetTools_SoEvaluatedOutputsStayUnchanged(string actionType)
    {
        using var h = Harness.Create();
        h.Handler.EnqueueReply("ok");

        await h.ChatAsync(UserA, "x", actionType);

        var body = Json(h.Handler.Requests.Single());
        Assert.False(body.TryGetProperty("tools", out _));
        Assert.False(body.TryGetProperty("tool_choice", out _));
    }

    [Theory]
    [InlineData("free")]
    [InlineData("")]
    [InlineData("something-else")]
    public async Task E_FreeFormChat_GetsTools(string actionType)
    {
        using var h = Harness.Create();
        h.Handler.EnqueueReply("ok");

        await h.ChatAsync(UserA, "x", actionType);

        Assert.Equal(3, Json(h.Handler.Requests.Single()).GetProperty("tools").GetArrayLength());
    }

    [Fact]
    public async Task E_Insights_NeverGetTools_AndKeepJsonMode()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueReply("""{"summary":"s","strengths":[],"improvements":[],"nextWorkoutSuggestion":"n"}""");

        using var scope = h.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAiWorkoutCoachService>().GetInsightsAsync(UserA);

        var body = Json(h.Handler.Requests.Single());
        Assert.False(body.TryGetProperty("tools", out _));
        Assert.Equal("json_object", body.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task E_ExistingChatBehavior_IsUnchanged_Temperature_Guardrail_Sanitization()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueToolCall("c1", "Workout-GetWeightTrends", "{}");
        // Tool sonrası final cevap da aynı sanitization + ACSM guardrail hattından geçer (Bench baseline 100 kg -> 110 kg).
        h.Handler.EnqueueReply("Bugün göğüs.\nBench Press: 3 set × 8 tekrar @ 125 kg\nSonuç olarak iyi.");

        var response = await h.ChatAsync(UserA, "bugün ne yapayım?");

        Assert.Equal(0.3, Json(h.Handler.Requests[0]).GetProperty("temperature").GetDouble(), 3);
        Assert.True(response.GuardrailTriggered);
        Assert.Contains("@ 110.0 kg", response.Reply);
        Assert.DoesNotContain("Sonuç olarak", response.Reply);
        Assert.Equal(new[] { "Squat" }, response.PlateauAlerts.ToArray()); // context'ten gelen plateau uyarıları
    }

    // ───────────────────── F. Cache regression ─────────────────────

    [Fact]
    public async Task F_ContextCache_StillWorks_AndToolCallsAreSeparateFreshReads()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueReply("bir");
        h.Handler.EnqueueReply("iki");

        await h.ChatAsync(UserA, "a");
        await h.ChatAsync(UserA, "b");
        Assert.Equal(1, h.Analysis.RequestedUserIds.Count); // ikinci chat cache'ten (1 dakika)

        h.Handler.EnqueueToolCall("c1", "Workout-GetPlateauExercises", "{}");
        h.Handler.EnqueueReply("üç");
        await h.ChatAsync(UserA, "c");
        Assert.Equal(2, h.Analysis.RequestedUserIds.Count); // cache hit (inject yok) + 1 tool okuması
    }

    // ───────────────────── G. Tool error handling ─────────────────────

    [Fact]
    public async Task G_ToolDataAccessFailure_DoesNotCrashChat_AndDoesNotLeakDetailsToModel()
    {
        using var h = Harness.Create();
        h.Analysis.ThrowOnCallNumber = 2; // 1. çağrı: context injection (başarılı), 2. çağrı: tool (hata)
        h.Handler.EnqueueToolCall("c1", "Workout-GetWeightTrends", "{}");
        h.Handler.EnqueueReply("Şu an trend verisine ulaşamadım ama genel olarak devam et.");

        var response = await h.ChatAsync(UserA, "trendlerim?");

        Assert.Equal("Şu an trend verisine ulaşamadım ama genel olarak devam et.", response.Reply);
        var toolContent = MessageText(Json(h.Handler.Requests[1]).GetProperty("messages").EnumerateArray().Last());
        Assert.Contains("temporarily unavailable", toolContent);
        Assert.DoesNotContain("SECRET", toolContent);   // exception mesajı / SQL detayı modele sızmaz
        Assert.DoesNotContain("SqlException", toolContent);
    }

    // ───────────────────── Reasoning (gpt-oss) must not leak ─────────────────────

    [Fact]
    public async Task Reasoning_ReturnedInSeparateField_IsNotLeakedToUser_AndNoUnsupportedParamsAreSent()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueReply("Nihai cevap.", reasoning: "GİZLİ-DÜŞÜNCE-ZİNCİRİ: kullanıcıya gösterme");

        var response = await h.ChatAsync(UserA, "selam");

        Assert.Equal("Nihai cevap.", response.Reply);
        Assert.DoesNotContain("GİZLİ", response.Reply);

        // gpt-oss reasoning_format'ı desteklemez; eski davranışa uygun şekilde reasoning parametresi gönderilmez.
        var body = Json(h.Handler.Requests.Single());
        Assert.False(body.TryGetProperty("reasoning_format", out _));
        Assert.False(body.TryGetProperty("include_reasoning", out _));
        Assert.False(body.TryGetProperty("reasoning_effort", out _));
    }

    // ───────────────────── DI lifetimes ─────────────────────

    [Fact]
    public void Di_Kernel_IsStillTransient_PluginIsScoped_AndNeverCapturedByRootProvider()
    {
        using var h = Harness.Create();

        // Scoped plugin bağımlılıkları singleton/root graph'a capture edilemez: root'tan Kernel çözümleme hata verir.
        Assert.Throws<InvalidOperationException>(() => h.Provider.GetRequiredService<Kernel>());

        using var scope1 = h.Provider.CreateScope();
        using var scope2 = h.Provider.CreateScope();
        scope1.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(UserA);

        var k1a = scope1.ServiceProvider.GetRequiredService<Kernel>();
        var k1b = scope1.ServiceProvider.GetRequiredService<Kernel>();
        var k2 = scope2.ServiceProvider.GetRequiredService<Kernel>();
        Assert.NotSame(k1a, k1b); // Kernel hâlâ transient

        // Scope'lar arası kullanıcı sızıntısı yok.
        Assert.Same(scope1.ServiceProvider.GetRequiredService<ICurrentUserContext>(), scope1.ServiceProvider.GetRequiredService<ICurrentUserContext>());
        Assert.Null(scope2.ServiceProvider.GetRequiredService<ICurrentUserContext>().UserId);
        Assert.NotNull(k2);
    }

    // ───────────────────── Harness ─────────────────────

    private static JsonElement Json(CapturedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Body);
        return doc.RootElement.Clone();
    }

    private static string MessageText(JsonElement message)
    {
        var content = message.GetProperty("content");
        return content.ValueKind == JsonValueKind.String
            ? content.GetString()!
            : string.Concat(content.EnumerateArray().Select(p => p.GetProperty("text").GetString()));
    }

    private sealed record CapturedRequest(Uri Uri, string Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();
        public List<CapturedRequest> Requests { get; } = new();

        public void EnqueueReply(string content, string? reasoning = null) => _responses.Enqueue(Completion(
            new { role = "assistant", content, reasoning }, "stop"));

        public void EnqueueToolCall(string id, string name, string argumentsJson) => _responses.Enqueue(Completion(
            new
            {
                role = "assistant",
                content = (string?)null,
                tool_calls = new[] { new { id, type = "function", function = new { name, arguments = argumentsJson } } }
            }, "tool_calls"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.RequestUri!, body));

            if (_responses.Count == 0)
                throw new InvalidOperationException("Test beklenmeyen bir Groq çağrısı yaptı (kuyrukta cevap yok).");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }

        // Gerçek Groq cevabına benzer (usage.queue_time, x_groq vb. dahil).
        private static string Completion(object message, string finishReason) => JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            @object = "chat.completion",
            created = 1700000000,
            model = "openai/gpt-oss-120b",
            choices = new[] { new { index = 0, message, logprobs = (object?)null, finish_reason = finishReason } },
            usage = new { queue_time = 0.01, prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 },
            x_groq = new { id = "req_test" }
        });
    }

    /// <summary>Kullanıcı başına ayrı veri döndüren sahte analiz servisi.</summary>
    private sealed class FakeAnalysisService : IWorkoutAnalysisService
    {
        private int _calls;

        public List<string> RequestedUserIds { get; } = new();
        public int? ThrowOnCallNumber { get; set; }

        public Dictionary<string, FitBotContextDto> Contexts { get; } = new()
        {
            [UserA] = new()
            {
                TotalWorkouts = 12,
                DaysSinceLastWorkout = 1,
                WorkoutsThisWeek = 2,
                WorkoutsLast30Days = 9,
                PlateauExercises = new() { "Squat" },
                WeightTrends = new()
                {
                    new()
                    {
                        ExerciseName = "Bench Press",
                        Trend = "UP",
                        WeeklyMaxWeights = new() { new() { WeeksAgo = 0, MaxKg = 100 }, new() { WeeksAgo = 2, MaxKg = 95 } }
                    }
                },
                RecentWorkouts = Enumerable.Range(0, 12).Select(i => new WorkoutContextEntryDto
                {
                    WorkoutName = $"Antrenman {i}",
                    WorkoutDate = new DateTime(2026, 5, 1).AddDays(-i),
                    Exercises = new() { new() { ExerciseName = "Bench Press", MaxWeightKg = 100, SetCount = 3, Reps = "8" } }
                }).ToList()
            },
            [UserB] = new()
            {
                TotalWorkouts = 3,
                WeightTrends = new()
                {
                    new()
                    {
                        ExerciseName = "B Only Exercise",
                        Trend = "UP",
                        WeeklyMaxWeights = new() { new() { WeeksAgo = 0, MaxKg = 777 }, new() { WeeksAgo = 2, MaxKg = 700 } }
                    }
                }
            }
        };

        public Task<WorkoutAnalysisDto> GetAnalysisAsync(string userId) =>
            Task.FromResult(new WorkoutAnalysisDto { TotalWorkouts = Contexts[userId].TotalWorkouts });

        public Task<FitBotContextDto> GetFitBotContextAsync(string userId)
        {
            RequestedUserIds.Add(userId);
            if (ThrowOnCallNumber == ++_calls)
                throw new InvalidOperationException("SECRET SqlException: connection string Server=prod;Password=hunter2");

            return Task.FromResult(Contexts[userId]);
        }
    }

    private sealed class Harness : IDisposable
    {
        public ServiceProvider Provider { get; }
        public StubHandler Handler { get; } = new();
        public FakeAnalysisService Analysis { get; } = new();

        private Harness(string culture)
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = "test-key",
                ["Groq:Model"] = "openai/gpt-oss-120b"
            }).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IAcsmGuardrailService, AcsmGuardrailService>();
            services.AddSingleton<IWorkoutAnalysisService>(Analysis);
            services.AddGroqSemanticKernel();
            services.AddFitBotWorkoutPlugin();               // Program.cs ile aynı kayıtlar
            services.AddScoped<IAiWorkoutCoachService, AiWorkoutCoachService>();
            services.AddHttpClient(GroqChatCompletion.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Handler);

            // Development ortamındaki gibi scope doğrulaması açık: scoped servislerin yanlış capture edilmesi test patlatır.
            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        public static Harness Create(string culture = "tr-TR") => new(culture);

        /// <summary>Her çağrı kendi request scope'unda çalışır (production'daki gibi).</summary>
        public async Task<FitBotChatResponseDto> ChatAsync(string userId, string message, string actionType = "free")
        {
            using var scope = Provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IAiWorkoutCoachService>();
            return await service.ChatAsync(userId, new FitBotChatRequestDto { Message = message, ActionType = actionType });
        }

        public void Dispose() => Provider.Dispose();
    }
}
