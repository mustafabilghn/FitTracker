using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CommunityToolkit.VectorData.InMemory;
using FitTrackr.API.Data;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Plugins;
using FitTrackr.API.RAG;
using FitTrackr.API.Repositories;
using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using FitTrackr.API.Validations;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// RAG'in mevcut function-calling mimarisiyle entegrasyonu: Program.cs ile aynı plugin kayıtları (Workout + WorkoutPlan +
/// Knowledge), gerçek WorkoutAnalysisService/Repository (SQLite in-memory) ve gerçek Semantic Kernel connector'ü.
/// Yalnızca LLM transport'u (scripted), vector store (in-memory) ve embedding (deterministik) sahtedir.
/// Model kararları scripted olduğundan bu testler "model X'i çağırırsa hat doğru çalışır mı"yı doğrular; gerçek
/// modelin seçim davranışı canlı smoke ile ayrıca doğrulanır.
/// </summary>
public class KnowledgeFunctionCallingTests
{
    private const string UserA = "user-A";
    private const string UserB = "user-B";
    private const string ToolName = "Knowledge-SearchFitnessKnowledge";

    // ───────────────────── Routing / registration ─────────────────────

    [Fact]
    public async Task FreeChat_OffersKnowledgeToolAlongsideExistingTools_AndAddsGroundingRules()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueReply("ok");

        await h.ChatAsync(UserA, "selam");

        var body = Json(h.Llm.Requests.Single());
        var tools = body.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("function").GetProperty("name").GetString()).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            ToolName, "Workout-GetPlateauExercises", "Workout-GetRecentWorkouts", "Workout-GetWeightTrends",
            "WorkoutPlan-GetPlannedWorkouts", "WorkoutPlan-SaveWorkoutPlan"
        }, tools);
        Assert.Equal("auto", body.GetProperty("tool_choice").GetString());

        var system = SystemPrompt(body);
        Assert.Contains("KULLANICI VERİSİ", system);                         // mevcut context injection aynen duruyor
        Assert.Contains("=== GENEL FİTNESS BİLGİSİ (SearchFitnessKnowledge) ===", system);
        Assert.Contains("kullanıcının antrenman geçmişi DEĞİLDİR", system);
        Assert.Contains("Kişisel geçmiş, performans, trend, plato veya planlı antrenman sorusu", system);
        Assert.Contains("kişisel tool sonucunun YERİNE GEÇMEZ", system);
        Assert.Contains("özel sayı, yüzde, aralık, eşik", system);
        Assert.Contains("ACSM %10", system);

        var knowledgeTool = body.GetProperty("tools").EnumerateArray()
            .Single(t => t.GetProperty("function").GetProperty("name").GetString() == ToolName)
            .GetProperty("function");
        Assert.Contains("never to answer a personal history", knowledgeTool.GetProperty("description").GetString());
        Assert.Contains("do not invent numbers", knowledgeTool.GetProperty("description").GetString());
    }

    [Theory]
    [InlineData("analyze")]
    [InlineData("today")]
    [InlineData("program")]
    [InlineData("motivation")]
    public async Task PresetActions_StillGetNoTools_AndNoRagRules(string actionType)
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueReply("ok");

        await h.ChatAsync(UserA, "x", actionType);

        var body = Json(h.Llm.Requests.Single());
        Assert.False(body.TryGetProperty("tools", out _));
        Assert.DoesNotContain("SearchFitnessKnowledge", SystemPrompt(body));
    }

    [Fact]
    public async Task RagDisabled_ToolsAndSystemPromptAreExactlyNonRag()
    {
        using var rag = ChatHarness.Create();
        using var nonRag = ChatHarness.Create(ragEnabled: false);
        rag.Llm.EnqueueReply("ok");
        nonRag.Llm.EnqueueReply("ok");

        await rag.ChatAsync(UserA, "selam");
        await nonRag.ChatAsync(UserA, "selam");

        var ragPrompt = SystemPrompt(Json(rag.Llm.Requests.Single()));
        var nonRagBody = Json(nonRag.Llm.Requests.Single());
        Assert.Equal(5, nonRagBody.GetProperty("tools").GetArrayLength());
        var nonRagPrompt = SystemPrompt(nonRagBody);
        Assert.DoesNotContain("SearchFitnessKnowledge", nonRagPrompt);
        // RAG yalnızca sona ek kural bölümü ekler. (Satırlar sıradan bağımsız karşılaştırılır: iki ayrı SQLite DB'de
        // bir antrenmandaki egzersizlerin sırası EF tarafından garanti edilmez; bu RAG ile ilgisizdir.)
        var marker = ragPrompt.IndexOf("=== GENEL FİTNESS BİLGİSİ", StringComparison.Ordinal);
        Assert.True(marker > 0);
        static string[] Lines(string s) => s.Trim().Split('\n').Select(l => l.TrimEnd('\r')).OrderBy(l => l, StringComparer.Ordinal).ToArray();
        Assert.Equal(Lines(nonRagPrompt), Lines(ragPrompt[..marker]));
    }

    // ───────────────────── 11. Personal question → existing tools ─────────────────────

    [Fact]
    public async Task PersonalQuestion_StillUsesExistingWorkoutTools_KnowledgeSearchNotTouched()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("c1", "Workout-GetWeightTrends", """{"exerciseName":"Bench Press"}""");
        h.Llm.EnqueueReply("Bench Press'te 95 kg'dan 100 kg'a çıktın.");

        var response = await h.ChatAsync(UserA, "Bench Press'te son haftalarda nasıl ilerledim?");

        Assert.Equal("Bench Press'te 95 kg'dan 100 kg'a çıktın.", response.Reply);
        Assert.Equal(2, h.Llm.Requests.Count);
        var tool = ToolMessages(h.Llm.Requests[1]).Single();
        Assert.Contains("Bench Press", tool);
        Assert.Contains("100", tool);
        Assert.Empty(h.Embeddings.Queries); // bilgi tabanı hiç sorgulanmadı
    }

    [Fact]
    public async Task PersonalPerformanceQuestion_IsContractuallyRequiredToUsePersonalTool()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("p1", "Workout-GetPlateauExercises", "{}");
        h.Llm.EnqueueReply("Bench Press platosu için kişisel verine göre değerlendirme yapıldı.");

        await h.ChatAsync(UserA, "Bench Press performansım ve platom hakkında ne düşünüyorsun?");

        Assert.Contains("Workout-GetPlateauExercises", h.Llm.Requests[0]);
        Assert.Empty(h.Embeddings.Queries);
    }

    [Fact]
    public async Task PersonalQuestionWithoutPersonalTool_IsRecoveredWithDeterministicPersonalTool()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueReply("Maks ağırlık üç hafta boyunca aynı kaldı. %10-15 artırabilirsin.");
        h.Llm.EnqueueReply("Squat verilerini kontrol ettim; mevcut kişisel kayıtlarına göre değerlendirme yapabilirim.");

        var response = await h.ChatAsync(UserA, "Squat performansımı ve trendimi değerlendir.");

        Assert.Contains("Squat verilerini kontrol ettim", response.Reply);
        Assert.Equal(2, h.Llm.Requests.Count);
        Assert.Empty(h.Embeddings.Queries);
    }

    [Fact]
    public async Task MixedQuestionWithoutKnowledgeRetrieval_IsRecoveredWithBothDeterministicTools()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("p1", "Workout-GetWeightTrends", """{"exerciseName":"Bench Press"}""");
        h.Llm.EnqueueReply("Maks ağırlık sabit. %10-15 azalt ve 4 set × 5 tekrar yap.");
        h.Llm.EnqueueReply("Kişisel trend verilerini ve genel bilgi kaynağını birlikte kontrol ettim.");

        var response = await h.ChatAsync(UserA, "Bench Press'te takıldım. Verilerime bakarak bunu nasıl aşabilirim?");

        Assert.Contains("birlikte kontrol ettim", response.Reply);
        // Sorgu kullanıcının gerçek sorusundan üretilir (sabit "plato aşma" yok); kişisel bağlam ifadesi çıkarılır.
        var query = h.Embeddings.Queries.Single();
        Assert.Contains("Bench Press", query);
        Assert.Contains("aşabilirim", query);
        Assert.Contains("plato", query);
        Assert.DoesNotContain("Verilerime", query);
        Assert.Equal(3, h.Llm.Requests.Count);
        // Model kişisel tool'u zaten çağırdığı için kurtarma kişisel tool'u tekrar çalıştırmadı (tek tool mesajı).
        Assert.Single(ToolMessages(h.Llm.Requests[1]));
        Assert.False(Json(h.Llm.Requests[2]).TryGetProperty("tools", out _)); // retry tool'suz: sonsuz döngü yok
    }

    [Fact]
    public async Task KnowledgeGroundingContract_RequiresUnsupportedNumbersToBeQualified()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"deload nedir"}""");
        h.Llm.EnqueueReply("Kaynakta belirli bir yüzde veya aralık belirtilmiyor.");

        var response = await h.ChatAsync(UserA, "Deload için kaynakta olmayan yüzde kaç azaltmalıyım?");

        Assert.Contains("belirtilmiyor", response.Reply);
        Assert.DoesNotContain("Kaynak: %30", response.Reply);
        Assert.DoesNotContain("Kaynak: %40", response.Reply);
    }

    // ───────────────────── 12. General knowledge → SearchFitnessKnowledge ─────────────────────

    [Fact]
    public async Task GeneralKnowledgeQuestion_CanUseSearchFitnessKnowledge_ResultsAreSourcedAndGoBackToModel()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"Deload nedir?"}""");
        h.Llm.EnqueueReply("Deload, yükün geçici olarak azaltıldığı dönemdir. Kaynak: Bell ve ark. 2023");

        var response = await h.ChatAsync(UserA, "Deload nedir?");

        Assert.Equal("Deload, yükün geçici olarak azaltıldığı dönemdir. Kaynak: Bell ve ark. 2023", response.Reply);
        Assert.Equal(new[] { "Deload nedir?" }, h.Embeddings.Queries.ToArray());

        using var tool = JsonDocument.Parse(ToolMessages(h.Llm.Requests[1]).Single());
        Assert.Equal("ok", tool.RootElement.GetProperty("status").GetString());
        var results = tool.RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.InRange(results.Count, 1, 3);
        // Sıralama sahte (bag-of-words) embedding'e bağlıdır; gerçek modelin sıralaması canlı smoke ile doğrulanır.
        var deload = Assert.Single(results, r => r.GetProperty("title").GetString() == "Deload nedir?");
        Assert.Equal("https://doi.org/10.1186/s40798-023-00633-0", deload.GetProperty("sourceUrl").GetString());
        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("sourceName").GetString())));
    }

    // ───────────────────── 13. Mixed question → personal tool + knowledge ─────────────────────

    [Fact]
    public async Task MixedQuestion_CanUsePersonalToolAndKnowledgeSearch_InOneTurn()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("p1", "Workout-GetWeightTrends", """{"exerciseName":"Bench Press"}""");
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"Bench Press plato aşma"}""");
        h.Llm.EnqueueReply("Son Bench Press maksimumun 100 kg; kısa bir deload ve tekrar aralığı değişikliği deneyebilirsin.");

        var response = await h.ChatAsync(UserA, "Bench Press'te plato yaşıyorum. Son performansıma bakarak neyi değiştirmemi önerirsin?");

        Assert.Equal(3, h.Llm.Requests.Count); // personal tool → knowledge tool → sentez
        var tools = ToolMessages(h.Llm.Requests[2]);
        Assert.Equal(2, tools.Count);
        Assert.Contains("Bench Press", tools[0]);                  // kişisel veri (SQL/function tool)
        using var knowledge = JsonDocument.Parse(tools[1]);       // genel bilgi (RAG)
        Assert.Equal("ok", knowledge.RootElement.GetProperty("status").GetString());
        Assert.All(knowledge.RootElement.GetProperty("results").EnumerateArray(),
            r => Assert.StartsWith("https://", r.GetProperty("sourceUrl").GetString()));
        Assert.Equal(new[] { "Bench Press plato aşma" }, h.Embeddings.Queries.ToArray());
        Assert.StartsWith("Son Bench Press maksimumun 100 kg", response.Reply);
        Assert.False(response.GuardrailTriggered); // 100 kg baseline içinde
    }

    [Fact]
    public async Task MixedQuestion_SanitizesUnsupportedNumericClaims_ButKeepsPersonalWeight()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("p1", "Workout-GetWeightTrends", """{"exerciseName":"Bench Press"}""");
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"Bench Press plato aşma"}""");
        h.Llm.EnqueueReply("Bench Press maksimumun 100 kg. Yükü %30-40 azalt ve %80-85 yoğunluk kullan. Ayrıca 10+ set ve 3-5 tekrar, 8-12 hafta boyunca %2-5 artış uygula.");

        var response = await h.ChatAsync(UserA, "Bench Press'te plato yaşıyorum. Son performansıma bakıp ne yapabileceğimi anlat.");

        Assert.Contains("100 kg", response.Reply);
        Assert.DoesNotContain("%30-40", response.Reply);
        Assert.DoesNotContain("%80-85", response.Reply);
        Assert.DoesNotContain("10+ set", response.Reply);
        Assert.DoesNotContain("3-5 tekrar", response.Reply);
        Assert.DoesNotContain("8-12 hafta", response.Reply);
        Assert.DoesNotContain("%2-5", response.Reply);
        Assert.DoesNotContain("spesifik bir değer", response.Reply);
        Assert.DoesNotContain("Retrieved kaynak kaldırılan sayısal ayrıntıyı belirtmiyor.", response.Reply);
        Assert.DoesNotContain("Retrieved kaynak sabit bir aralık önermiyor.", response.Reply);
    }

    [Fact]
    public async Task UnsupportedNumericSentence_IsRemoved_WhileSafeSentenceIsKept()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"Bench Press plato aşma"}""");
        h.Llm.EnqueueReply("Haftada 10+ set yapabilirsin. Formuna dikkat et ve toparlanmanı izle.");

        var response = await h.ChatAsync(UserA, "Bench Press platosu için ne yapabilirim?");

        Assert.DoesNotContain("10+", response.Reply);
        Assert.DoesNotContain("spesifik bir değer", response.Reply);
        Assert.Contains("Formuna dikkat et ve toparlanmanı izle.", response.Reply);
        Assert.DoesNotContain("Retrieved kaynak", response.Reply);
    }

    [Fact]
    public async Task UnsupportedNumericOnlyResponse_UsesNaturalUserFacingFallback()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"Bench Press plato aşma"}""");
        h.Llm.EnqueueReply("Yükü %30-40 azalt ve %80-85 yoğunluk kullan.");

        var response = await h.ChatAsync(UserA, "Bench Press platosu için ne yapabilirim?");

        Assert.Contains("toparlan", response.Reply);
        Assert.DoesNotContain("spesifik bir değer", response.Reply);
        Assert.DoesNotContain("Retrieved kaynak", response.Reply);
        Assert.DoesNotContain("sayısal ayrıntı", response.Reply);
    }

    [Fact]
    public async Task DeloadRange_IsKeptOnlyWhenTheSourceCaveatIsPreserved()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"deload zamanlama"}""");
        h.Llm.EnqueueReply("Literatürde 4-6 haftalık aralıklar geçiyor, ancak Bell paneli sabit bir aralık önermedi.");

        var response = await h.ChatAsync(UserA, "Deload ne zaman düşünülür?");

        Assert.Contains("4-6", response.Reply);
        Assert.Contains("sabit bir aralık önermedi", response.Reply);
    }

    [Fact]
    public async Task DeloadRange_PrescriptiveMeaningIsReplacedWithSourceCaveat()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"deload zamanlama"}""");
        h.Llm.EnqueueReply("Her 4-6 haftada bir deload yapmalısın.");

        var response = await h.ChatAsync(UserA, "Deload ne zaman düşünülür?");

        Assert.DoesNotContain("4-6", response.Reply);
        Assert.Contains("toparlan", response.Reply);
        Assert.DoesNotContain("Retrieved kaynak", response.Reply);
    }

    // ───────────────────── 9. Personal data never flows through knowledge search ─────────────────────

    [Fact]
    public async Task KnowledgeSearch_NeverExposesCurrentOrOtherUsersPersonalData()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"Zercher Squat ilerleme nasıl olmalı"}""");
        h.Llm.EnqueueReply("ok");

        await h.ChatAsync(UserA, "Zercher Squat'ta nasıl ilerlerim?");

        var toolContent = ToolMessages(h.Llm.Requests[1]).Single();
        foreach (var personal in new[] { "87.5", "87,5", "B Secret Exercise", "777", UserA, UserB })
            Assert.DoesNotContain(personal, toolContent);

        // Embed edilen metin yalnızca modelin sorgusu: sunucu sorguya kullanıcı verisi eklemez.
        Assert.Equal(new[] { "Zercher Squat ilerleme nasıl olmalı" }, h.Embeddings.Queries.ToArray());

        // Vector store yalnızca corpus içeriği barındırır.
        var corpus = FitnessKnowledgeCorpusLoader.LoadEmbedded();
        var collection = h.Provider.GetRequiredService<FitnessKnowledgeCollectionProvider>().Get();
        var count = 0;
        await foreach (var record in collection.GetAsync(r => r.ChunkId != "", 1000))
        {
            count++;
            Assert.Equal(corpus.Corpus, record.Corpus);
            Assert.Contains(corpus.Items, i => i.Id == record.ChunkId && i.Text == record.Text);
            Assert.DoesNotContain("B Secret", record.Text);
        }
        Assert.Equal(corpus.Items.Count, count);
    }

    // ───────────────────── 10. Tool failure inside a chat ─────────────────────

    [Fact]
    public async Task KnowledgeBackendDown_ChatStillAnswers_ModelGetsGenericError_NoInternals()
    {
        using var h = ChatHarness.Create();
        h.Embeddings.FailWith = new HttpRequestException("No connection could be made (localhost:11434); Qdrant localhost:6334 SECRET");
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"deload"}""");
        h.Llm.EnqueueReply("Bilgi tabanına şu an ulaşamadım; genel olarak yükü geçici azaltmak bir seçenek.");

        var response = await h.ChatAsync(UserA, "Deload nedir?");

        Assert.Equal("Bilgi tabanına şu an ulaşamadım; genel olarak yükü geçici azaltmak bir seçenek.", response.Reply);
        var tool = ToolMessages(h.Llm.Requests[1]).Single();
        Assert.Contains("\"status\":\"unavailable\"", tool);
        foreach (var secret in new[] { "11434", "6334", "Qdrant", "SECRET", "HttpRequestException", "localhost" })
            Assert.DoesNotContain(secret, tool);
    }


    [Fact]
    public async Task KnowledgeRecovery_WhenModelSkipsToolAndBackendIsDown_DegradesToNonRagAnswer_WithoutFabricatedSource()
    {
        using var h = ChatHarness.Create();

        // RAG backend'i erişilemez: deterministik kurtarma sırasında hata oluşacak.
        h.Embeddings.FailWith =
            new HttpRequestException("localhost:11434; Qdrant localhost:6334 SECRET");

        // Model ilk turda bilgi aracını çağırmadan yanıt üretiyor ve (uydurma) bir kaynak satırı ekliyor.
        h.Llm.EnqueueReply("Deload, antrenman yükünü azaltmaya yönelik bir yaklaşımdır.\nKaynak: ACSM 2026");

        var response = await h.ChatAsync(UserA, "Deload nedir?");

        // Servis erişilemezken cevap gereksiz yere reddedilmez (NON-RAG davranışı), ama retrieval olmadığı için
        // kaynaklı görünen atıf satırı kaldırılır.
        Assert.Equal("Deload, antrenman yükünü azaltmaya yönelik bir yaklaşımdır.", response.Reply);

        // Kurtarma mekanizması RAG sorgusunu kullanıcının gerçek sorusuyla gerçekten denemiş olmalı.
        Assert.Equal(
            new[] { "Deload nedir?" },
            h.Embeddings.Queries.ToArray());

        // İlk model yanıtı dışında başka bir LLM çağrısı yapılmamalı.
        // Deterministik kurtarma, aracı doğrudan sunucu tarafında çağırır.
        Assert.Single(h.Llm.Requests);

        // İç altyapı ayrıntıları kullanıcı yanıtına sızmamalı.
        foreach (var secret in new[]
        {
        "11434", "6334", "Qdrant", "SECRET", "localhost"
    })
        {
            Assert.DoesNotContain(secret, response.Reply);
        }
    }


    // ───────────────────── 15. RAG disabled: normal FitBot behavior is not blocked ─────────────────────

    [Fact]
    public async Task RagDisabled_KnowledgeTopicQuestion_IsAnsweredNormally_NoRefusal_NoExtraLlmCall()
    {
        using var h = ChatHarness.Create(ragEnabled: false);
        h.Llm.EnqueueReply("Deload, yükün geçici olarak azaltıldığı bir dönemdir. Haftada 3 gün antrenman yapıyorsan yorgunluğu izle.");

        var response = await h.ChatAsync(UserA, "Deload haftası nedir ve ne zaman yapılmalı? Bana bir strateji öner.");

        Assert.Equal("Deload, yükün geçici olarak azaltıldığı bir dönemdir. Haftada 3 gün antrenman yapıyorsan yorgunluğu izle.", response.Reply);
        Assert.Single(h.Llm.Requests);
        Assert.Empty(h.Embeddings.Queries);
    }

    [Fact]
    public async Task RagDisabled_PersonalQuestionWithoutTool_IsStillRecoveredWithPersonalTool()
    {
        using var h = ChatHarness.Create(ragEnabled: false);
        h.Llm.EnqueueReply("Tahmini özet.");
        h.Llm.EnqueueReply("Son antrenmanlarında Bench Press 100 kg'a çıktı.");

        var response = await h.ChatAsync(UserA, "Son antrenmanlarımı kısaca özetler misin?");

        Assert.Equal("Son antrenmanlarında Bench Press 100 kg'a çıktı.", response.Reply);
        Assert.Equal(2, h.Llm.Requests.Count);
        Assert.Contains("Workout-GetRecentWorkouts", h.Llm.Requests[1]); // doğrulanmış tool sonucu retry'a eklendi
        Assert.Empty(h.Embeddings.Queries);
    }

    [Fact]
    public async Task GenericWorkoutSuggestion_WithOnlyOnerWord_DoesNotForceKnowledgeSearch()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueReply("Squat, Romanian Deadlift ve Lunge içeren bir bacak antrenmanı deneyebilirsin.");

        var response = await h.ChatAsync(UserA, "Bana bir bacak antrenmanı öner ama kaydetme.");

        Assert.Equal("Squat, Romanian Deadlift ve Lunge içeren bir bacak antrenmanı deneyebilirsin.", response.Reply);
        Assert.Single(h.Llm.Requests);
        Assert.Empty(h.Embeddings.Queries);
        Assert.Equal(0, await h.NewWorkoutCountAsync());
    }

    // ───────────────────── 16. Failed retrieval must not act like a found source ─────────────────────

    [Fact]
    public async Task KnowledgeUnavailable_AfterModelCalledTool_NumbersAreNotStripped_NoRetry()
    {
        using var h = ChatHarness.Create();
        h.Embeddings.FailWith = new HttpRequestException("connection refused");
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"setler arası dinlenme"}""");
        h.Llm.EnqueueReply("Setler arasında genelde 2-3 dakika dinlenebilirsin. Formuna dikkat et.");

        var response = await h.ChatAsync(UserA, "Setler arasında ne kadar dinlenmeliyim?");

        Assert.Equal("Setler arasında genelde 2-3 dakika dinlenebilirsin. Formuna dikkat et.", response.Reply);
        Assert.Contains("\"status\":\"unavailable\"", ToolMessages(h.Llm.Requests[1]).Single());
        Assert.Equal(2, h.Llm.Requests.Count);          // tool turu + cevap; ek retry yok
        Assert.Single(h.Embeddings.Queries);            // aynı tool kurtarmada ikinci kez çağrılmadı
    }

    [Fact]
    public async Task KnowledgeNoResults_NumbersAreKept_ButFabricatedSourceLineIsRemoved()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"bitcoin fiyatı kripto borsa"}""");
        h.Llm.EnqueueReply("Isınma için 5-10 dakika hafif kardiyo yapabilirsin.\nKaynak: ACSM 2026");

        var response = await h.ChatAsync(UserA, "Ağır setlerden önce nasıl ısınmalıyım?");

        Assert.Contains("\"status\":\"no_results\"", ToolMessages(h.Llm.Requests[1]).Single());
        Assert.Equal("Isınma için 5-10 dakika hafif kardiyo yapabilirsin.", response.Reply);
    }

    [Fact]
    public async Task SuccessfulRetrieval_KeepsSourceCitation_AndStillAppliesNumericGrounding()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"Deload nedir?"}""");
        h.Llm.EnqueueReply("Deload, yükün geçici olarak azaltıldığı dönemdir. Yükü %37 azalt.\nKaynak: Bell ve ark. 2023");

        var response = await h.ChatAsync(UserA, "Deload nedir?");

        Assert.Contains("Deload, yükün geçici olarak azaltıldığı dönemdir.", response.Reply);
        Assert.Contains("Kaynak: Bell ve ark. 2023", response.Reply);  // gerçek retrieval vardı: atıf korunur
        Assert.DoesNotContain("%37", response.Reply);                    // kaynakta olmayan sayı yine kaldırılır
    }

    // ───────────────────── 17. Deterministic tool recovery ─────────────────────

    [Fact]
    public async Task KnowledgeRecovery_ModelSkipsTool_ServerSearchesOnceWithRealQuestion_ThenOneToollessRetry()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueReply("Kaynaksız ilk cevap.");
        h.Llm.EnqueueReply("Deload, yükün planlı olarak geçici azaltıldığı bir dönemdir.");

        var response = await h.ChatAsync(UserA, "Deload haftası nedir?");

        Assert.Equal(new[] { "Deload haftası nedir?" }, h.Embeddings.Queries.ToArray());
        Assert.Equal(2, h.Llm.Requests.Count);
        var retry = Json(h.Llm.Requests[1]);
        Assert.False(retry.TryGetProperty("tools", out _));                 // retry'da tool sunulmaz → döngü yok
        Assert.Contains(ToolName, h.Llm.Requests[1]);                       // doğrulanmış sonuç bağlam olarak eklendi
        Assert.Equal("Deload, yükün planlı olarak geçici azaltıldığı bir dönemdir.", response.Reply);
    }

    [Fact]
    public async Task KnowledgeRecovery_DoesNotSearchAgain_WhenModelAlreadyCalledKnowledgeTool()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"deload"}""");
        h.Llm.EnqueueReply("Deload, yükün geçici olarak azaltıldığı dönemdir.");

        await h.ChatAsync(UserA, "Deload haftası nedir?");

        Assert.Equal(new[] { "deload" }, h.Embeddings.Queries.ToArray());
        Assert.Equal(2, h.Llm.Requests.Count);
    }

    [Fact]
    public async Task PersonalRecovery_DoesNotRunSecondPersonalTool_WhenModelAlreadyUsedAnotherPersonalTool()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("p1", "Workout-GetPlateauExercises", "{}");
        h.Llm.EnqueueReply("Squat değerlendirmesi kişisel verine göre yapıldı.");

        var response = await h.ChatAsync(UserA, "Squat performansımı ve trendimi değerlendir.");

        Assert.Equal("Squat değerlendirmesi kişisel verine göre yapıldı.", response.Reply);
        Assert.Equal(2, h.Llm.Requests.Count);
        Assert.Equal(0, h.Llm.PendingResponses);
    }

    [Fact]
    public async Task Recovery_NeverInvokesSaveWorkoutPlan()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueReply("Bacak planını kaydettim!");   // model tool çağırmadan yanlış başarı iddia ediyor
        h.Llm.EnqueueReply("Kişisel verilerine baktım.");

        await h.ChatAsync(UserA, "Son antrenmanlarıma bakarak bir bacak planı oluştur ve kaydet.");

        Assert.Equal(0, await h.NewWorkoutCountAsync());  // kurtarma yalnızca okuma tool'u çalıştırır
    }

    // ───────────────────── 18. English intent + messages ─────────────────────

    [Fact]
    public async Task English_PersonalQuestion_WhenPersonalDataUnavailable_RefusesInEnglish()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueReply("ok");
        await h.ChatAsync(UserA, "selam");   // context cache'e alınır
        h.BreakDatabase();                   // kişisel tool artık hata döndürür

        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        h.Llm.EnqueueToolCall("p1", "Workout-GetRecentWorkouts", "{}");
        h.Llm.EnqueueReply("You trained a lot last week!");

        var response = await h.ChatAsync(UserA, "Can you summarize my workouts?");

        Assert.Equal("I can't give a reliable personal assessment without checking your workout data. Please try again in a moment.", response.Reply);
        Assert.DoesNotContain("Kişisel", response.Reply);
    }

    [Fact]
    public async Task English_KnowledgeQuestion_TriggersRecoveryWithEnglishQuery_AndIsNotRefused()
    {
        using var h = ChatHarness.Create();
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        h.Llm.EnqueueReply("A deload is a planned short period of reduced training load.");

        var response = await h.ChatAsync(UserA, "What is a deload week and when should I do one?");

        Assert.Equal(new[] { "What is a deload week and when should I do one?" }, h.Embeddings.Queries.ToArray());
        Assert.StartsWith("A deload is a planned short period of reduced training load.", response.Reply);
        Assert.DoesNotContain("doğrulamadan", response.Reply);
    }

    [Theory]
    [InlineData("Son antrenmanlarımı kısaca özetler misin?", true)]
    [InlineData("Bench Press ilerlememe göre gelecek hafta ağırlığı ne kadar artırmalıyım?", true)]
    [InlineData("Kayıtlı antrenman planlarım neler?", true)]
    [InlineData("Squat'ta takıldığımı düşünüyorum.", true)]
    [InlineData("How is my progress on bench press?", true)]
    [InlineData("Have I plateaued on squat?", true)]
    [InlineData("I'm stuck on squat, what should I do?", true)]
    [InlineData("Squat'taki takılmayı aşmak için setler arası dinlenme süremi değiştirmeli miyim?", true)]
    [InlineData("Bench'teki takılmamı nasıl aşarım?", true)]
    [InlineData("Setlerimi ve tekrarlarımı nasıl düzenlemeliyim?", true)]
    [InlineData("Should I change my rest time between squat sets?", true)]
    [InlineData("Are my sets and reps right for hypertrophy?", true)]
    [InlineData("Setler arası dinlenme süresi ne kadar olmalı?", false)]
    [InlineData("How long should rest periods be for strength?", false)]
    [InlineData("Bana bir bacak antrenmanı öner ama kaydetme.", false)]
    [InlineData("Benim için iyi bir ısınma rutini nedir?", false)]
    [InlineData("What is progressive overload?", false)]
    [InlineData("selam", false)]
    public void LooksPersonal_MatchesOnlyFirstPersonDataRequests(string message, bool expected) =>
        Assert.Equal(expected, AiWorkoutCoachService.LooksPersonal(message));

    [Theory]
    [InlineData("Deload haftası nedir ve ne zaman yapılmalı?", true)]
    [InlineData("Progressive overload nedir, güvenli şekilde nasıl uygulanır?", true)]
    [InlineData("Güç antrenmanında setler arasında ne kadar dinlenmeliyim?", true)]
    [InlineData("Squat yaparken dizlerim içe kaçıyor, tekniğimi nasıl düzeltebilirim?", true)]
    [InlineData("Ağır setlerden önce nasıl ısınmalıyım?", true)]
    [InlineData("Squat'ta takıldığımı düşünüyorum. Verilerime bakarak bunu nasıl aşabileceğimi söyler misin?", true)]
    [InlineData("Bench Press ilerlememe göre gelecek hafta ağırlığı ne kadar artırmalıyım?", true)]
    [InlineData("How long should I rest between sets?", true)]
    [InlineData("How do I break through a plateau?", true)]
    [InlineData("What is progressive overload?", true)]
    [InlineData("Bana bir bacak antrenmanı öner ama kaydetme.", false)]
    [InlineData("Yolda yürürken ne dinlemeliyim?", false)]
    [InlineData("Bana bir program hazırlasam nasıl olur?", false)]
    [InlineData("Yarın için bir push antrenman planı oluştur ve kaydet: Bench Press 3 set x 8 tekrar 100 kg.", false)]
    [InlineData("Can you summarize my workouts?", false)]
    [InlineData("selam", false)]
    public void LooksKnowledgeSeeking_MatchesCorpusTopicsNotGenericWords(string message, bool expected) =>
        Assert.Equal(expected, AiWorkoutCoachService.LooksKnowledgeSeeking(message));

    [Fact]
    public void BuildKnowledgeQuery_UsesRealQuestion_StripsPersonalNumbersAndContext()
    {
        var query = AiWorkoutCoachService.BuildKnowledgeQuery(
            "Bench Press'te 100 kg ile 3x5 yapıyorum, %80 yoğunlukta; verilerime bakarak bunu nasıl aşabilirim?");

        Assert.Contains("Bench Press", query);
        Assert.Contains("nasıl aşabilirim?", query);
        foreach (var personal in new[] { "100", "kg", "3x5", "%80", "verilerime bakarak" })
            Assert.DoesNotContain(personal, query);

        Assert.Equal("Güç antrenmanında setler arasında ne kadar dinlenmeliyim?",
            AiWorkoutCoachService.BuildKnowledgeQuery("Güç antrenmanında setler arasında ne kadar dinlenmeliyim?"));
        Assert.DoesNotContain("plato", AiWorkoutCoachService.BuildKnowledgeQuery("Ağır setlerden önce nasıl ısınmalıyım?"));
        Assert.Equal(string.Empty, AiWorkoutCoachService.BuildKnowledgeQuery("   "));
    }

    // ───────────────────── 20. Grounding end-to-end (retrieval + personal data + ACSM) ─────────────────────

    [Fact]
    public async Task AcsmValidatedPersonalWeight_IsKept_AfterRetrieval_UnsupportedClaimRemoved()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("p1", "Workout-GetWeightTrends", """{"exerciseName":"Bench Press"}""");
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"Bench Press plato aşma"}""");
        h.Llm.EnqueueReply("Son Bench Press maksimumun 100 kg.\n\n- **Yük**: Gelecek hafta Bench Press'te 105 kg deneyebilirsin.\n- **Hacim**: Yükü %37 azalt.");

        var response = await h.ChatAsync(UserA, "Bench Press ilerlememe göre gelecek hafta ağırlığı ne kadar artırmalıyım?");

        Assert.Contains("Son Bench Press maksimumun 100 kg.", response.Reply);                         // kişisel veri
        Assert.Contains("- **Yük**: Gelecek hafta Bench Press'te 105 kg deneyebilirsin.", response.Reply); // ACSM ile doğrulanmış (100 → ≤110)
        Assert.DoesNotContain("%37", response.Reply);                                                    // kaynaksız iddia
        Assert.DoesNotContain("**Hacim**", response.Reply);                                              // yarım madde bırakılmaz
        Assert.False(response.GuardrailTriggered);
    }

    [Fact]
    public async Task UnsafeUnsourcedWeight_IsNotPreserved_EvenWithRetrieval()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"Bench Press plato aşma"}""");
        h.Llm.EnqueueReply("Bench Press'te hemen 125 kg dene. Formuna dikkat et.");

        var response = await h.ChatAsync(UserA, "Bench Press platosu için ne yapabilirim?");

        Assert.DoesNotContain("125", response.Reply);
        Assert.Contains("Formuna dikkat et.", response.Reply);
    }

    [Fact]
    public async Task InternalModelMarkup_NeverReachesTheUser()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"Deload nedir?"}""");
        h.Llm.EnqueueReply("Deload, yükün geçici olarak azaltıldığı dönemdir【sourceName: Bell et al. (2023)】.");

        var response = await h.ChatAsync(UserA, "Deload nedir?");

        Assert.Equal("Deload, yükün geçici olarak azaltıldığı dönemdir.", response.Reply);
    }

    [Fact]
    public async Task PersonalRestPlateauQuestion_ModelSkipsTools_BackendRunsPersonalAndKnowledgeOnce()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueReply("Kaynaksız ilk cevap.");
        h.Llm.EnqueueReply("Kişisel plato verin ve dinlenme bilgisiyle değerlendirme yapıldı.");

        var response = await h.ChatAsync(UserA, "Squat'taki takılmayı aşmak için setler arası dinlenme süremi değiştirmeli miyim?");

        Assert.Equal(2, h.Llm.Requests.Count);
        Assert.Contains("Workout-GetPlateauExercises", h.Llm.Requests[1]); // kişisel SQL/tool kanıtı retry'a eklendi
        Assert.Single(h.Embeddings.Queries);                              // bilgi araması bir kez
        Assert.Equal("Kişisel plato verin ve dinlenme bilgisiyle değerlendirme yapıldı.", response.Reply);
    }

    // ───────────────────── 19. Reasoning models: empty content / reasoning effort ─────────────────────

    [Fact]
    public async Task EmptyModelContent_IsNeverReturnedToUser_DeterministicFallback()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueReply("");   // gpt-oss: reasoning bütçesi bitti (finish_reason=length), içerik boş

        var response = await h.ChatAsync(UserA, "selam");

        Assert.Equal("Bu sefer bir yanıt oluşturamadım. Lütfen sorunu birazdan tekrar sor.", response.Reply);
        Assert.Single(h.Llm.Requests);
    }

    [Fact]
    public async Task ReasoningEffort_IsSentOnlyWhenConfigured()
    {
        using var configured = ChatHarness.Create(reasoningEffort: "low");
        using var standard = ChatHarness.Create();
        configured.Llm.EnqueueReply("ok");
        standard.Llm.EnqueueReply("ok");

        await configured.ChatAsync(UserA, "selam");
        await standard.ChatAsync(UserA, "selam");

        Assert.Equal("low", Json(configured.Llm.Requests.Single()).GetProperty("reasoning_effort").GetString());
        Assert.False(Json(standard.Llm.Requests.Single()).TryGetProperty("reasoning_effort", out _));
    }

    // ───────────────────── 14. SaveWorkoutPlan guardrail unchanged ─────────────────────

    private const string UnsafeLegsPlanJson = """
        {"workoutName":"Legs","exercises":[{"exerciseName":"Squat","sets":[{"reps":5,"weightInKg":150},{"reps":5,"weightInKg":140}]}]}
        """;

    [Fact]
    public async Task SaveWorkoutPlan_GuardrailStillRejectsUnsafePlan_EvenAfterKnowledgeWasRetrieved()
    {
        using var h = ChatHarness.Create();
        // Model önce bilgi tabanına bakar (ör. "progressive overload"), sonra güvensiz bir plan kaydetmeye çalışır.
        h.Llm.EnqueueToolCall("k1", ToolName, """{"query":"progressive overload ağırlık artışı"}""");
        h.Llm.EnqueueToolCall("s1", "WorkoutPlan-SaveWorkoutPlan", UnsafeLegsPlanJson);
        h.Llm.EnqueueReply("Planını kaydettim!"); // yanlış başarı beyanı hazır: kullanılmamalı

        var response = await h.ChatAsync(UserA, "Progressive overload'a göre bana bacak planı oluştur ve kaydet.");

        // RAG olmayan harness'taki (WorkoutPlanPersistenceTests.Hardening4) birebir aynı sunucu cevabı.
        Assert.Equal("Plan kaydedilmedi. Squat için önerilen ağırlık güvenli ilerleme sınırını (132.0 kg) aşıyor.", response.Reply);
        Assert.True(response.GuardrailTriggered);
        Assert.Equal(new[] { "Squat: 150.0 kg > 132.0 kg (ACSM ≤10% rule, plan not saved)" }, response.InterceptedProgressions.ToArray());
        Assert.Equal(0, await h.NewWorkoutCountAsync());
        Assert.Equal(2, h.Llm.Requests.Count);      // write sonrası LLM'e geri dönülmedi
        Assert.Equal(1, h.Llm.PendingResponses);
    }

    [Fact]
    public async Task SaveWorkoutPlan_SafePlan_StillSavesWithDeterministicReply_WhenRagIsEnabled()
    {
        using var h = ChatHarness.Create();
        h.Llm.EnqueueToolCall("s1", "WorkoutPlan-SaveWorkoutPlan",
            """{"workoutName":"Push","exercises":[{"exerciseName":"Bench Press","sets":[{"reps":8,"weightInKg":105},{"reps":8,"weightInKg":105}]}]}""");

        var response = await h.ChatAsync(UserA, "Bana bir push antrenmanı oluştur ve kaydet.");

        Assert.Equal("\"Push\" antrenman planın başarıyla kaydedildi (1 egzersiz, 2 set).", response.Reply);
        Assert.Equal(1, await h.NewWorkoutCountAsync());
        Assert.Empty(h.Embeddings.Queries);
    }

    // ───────────────────── Helpers ─────────────────────

    private static JsonElement Json(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static string MessageText(JsonElement message)
    {
        var content = message.GetProperty("content");
        return content.ValueKind == JsonValueKind.String
            ? content.GetString()!
            : string.Concat(content.EnumerateArray().Select(p => p.GetProperty("text").GetString()));
    }

    private static string SystemPrompt(JsonElement body) => MessageText(body.GetProperty("messages")[0]);

    private static List<string> ToolMessages(string body) =>
        Json(body).GetProperty("messages").EnumerateArray()
            .Where(m => m.GetProperty("role").GetString() == "tool")
            .Select(MessageText)
            .ToList();

    private sealed class ScriptedLlm : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();
        public List<string> Requests { get; } = new();
        public int PendingResponses => _responses.Count;

        public void EnqueueReply(string content) => _responses.Enqueue(Completion(new { role = "assistant", content }, "stop"));

        public void EnqueueToolCall(string id, string name, string argumentsJson) => _responses.Enqueue(Completion(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = new[] { new { id, type = "function", function = new { name, arguments = argumentsJson } } }
        }, "tool_calls"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            if (_responses.Count == 0)
                throw new InvalidOperationException("Test beklenmeyen bir Groq çağrısı yaptı (kuyrukta cevap yok).");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json") };
        }

        private static string Completion(object message, string finishReason) => JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            @object = "chat.completion",
            created = 1700000000,
            model = "openai/gpt-oss-120b",
            choices = new[] { new { index = 0, message, logprobs = (object?)null, finish_reason = finishReason } },
            usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 }
        });
    }

    private sealed class ChatHarness : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ServiceProvider Provider { get; }
        public ScriptedLlm Llm { get; } = new();
        public HashingEmbeddingService Embeddings { get; } = new();

        private ChatHarness(bool ragEnabled, string? reasoningEffort)
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = "test-key",
                ["Groq:Model"] = "openai/gpt-oss-120b",
                ["Rag:Enabled"] = ragEnabled ? "true" : "false",
                ["Rag:CollectionName"] = "chat_test_" + Guid.NewGuid().ToString("N"),
                ["Rag:MinRelevanceScore"] = "0.2", // deterministik bag-of-words embedding için
                ["Rag:CitationMinScore"] = "0.2",   // sahte embedding skorları gerçek cosine ölçeğinde değil; kapı ayrıca birim testlerle sınanır
                ["Rag:UnavailableCooldownSeconds"] = "0",
                ["Groq:ReasoningEffort"] = reasoningEffort
            }).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddDbContext<FitTrackrDbContext>(o => o.UseSqlite(_connection));
            services.AddScoped<IWorkoutRepository, WorkoutRepository>();          // Program.cs ile aynı
            services.AddScoped<IWorkoutAnalysisService, WorkoutAnalysisService>();
            services.AddSingleton<IAcsmGuardrailService, AcsmGuardrailService>();
            services.AddValidatorsFromAssemblyContaining<WorkoutRequestDtoValidator>();
            services.AddGroqSemanticKernel();
            services.AddFitBotWorkoutPlugin();
            services.AddFitBotWorkoutPlanPlugin();
            services.AddSingleton<IFitnessEmbeddingService>(Embeddings);                         // Ollama yerine
            services.AddKeyedSingleton<VectorStore>(RagServiceKeys.VectorStore, new InMemoryVectorStore()); // Qdrant yerine
            services.AddFitBotKnowledgePlugin(configuration);
            services.AddScoped<IAiWorkoutCoachService, AiWorkoutCoachService>();
            services.AddHttpClient(GroqChatCompletion.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Llm);

            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            db.Database.EnsureCreated();
            Seed(db, UserA, 10, ("Bench Press", 95));
            Seed(db, UserA, 3, ("Bench Press", 100), ("Squat", 120), ("Zercher Squat", 87.5));
            Seed(db, UserB, 3, ("B Secret Exercise", 777));
            db.SaveChanges();

            if (ragEnabled)
                Provider.GetRequiredService<IKnowledgeIngestionService>().IngestAsync().GetAwaiter().GetResult();
        }

        public static ChatHarness Create(bool ragEnabled = true, string? reasoningEffort = null) => new(ragEnabled, reasoningEffort);

        private static void Seed(FitTrackrDbContext db, string userId, int daysAgo, params (string Name, double Kg)[] exercises) =>
            db.Workouts.Add(new Workout
            {
                WorkoutName = "Seed",
                WorkoutDate = DateTime.UtcNow.Date.AddDays(-daysAgo),
                userId = userId,
                Exercises = exercises.Select(e => new Exercise
                {
                    ExerciseName = e.Name,
                    IntensityId = IntensitySeedIds.Medium,
                    ExerciseSets = new() { new ExerciseSet { SetNumber = 1, Reps = "5", WeightInKg = e.Kg } }
                }).ToList()
            });

        public async Task<FitBotChatResponseDto> ChatAsync(string userId, string message, string actionType = "free")
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IAiWorkoutCoachService>()
                .ChatAsync(userId, new FitBotChatRequestDto { Message = message, ActionType = actionType });
        }

        /// <summary>SQLite in-memory bağlantısını kapatır: sonraki DB erişimleri (kişisel tool'lar) hata verir.</summary>
        public void BreakDatabase() => _connection.Close();

        public async Task<int> NewWorkoutCountAsync()
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            return await db.Workouts.CountAsync(w => w.WorkoutName != "Seed");
        }

        public void Dispose()
        {
            Provider.Dispose();
            _connection.Dispose();
        }
    }
}
