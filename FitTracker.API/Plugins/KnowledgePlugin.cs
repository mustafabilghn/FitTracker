using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FitTrackr.API.RAG;
using Microsoft.SemanticKernel;

namespace FitTrackr.API.Plugins
{
    /// <summary>
    /// FitBot'un genel (kişisel olmayan) fitness bilgi tabanı araması (RAG). Kişisel antrenman verisinin YERİNE geçmez:
    /// kullanıcıya ait bilgiler yalnızca <see cref="WorkoutPlugin"/>/<see cref="WorkoutPlanPlugin"/> ve injected context'ten gelir.
    ///
    /// Güvenlik: Tek argüman <c>query</c>'dir; <c>userId</c>, profil veya veritabanı kimliği ALMAZ ve hiçbir kullanıcı
    /// verisine erişmez (bağımlılıkları yalnızca embedding + vector store). Backend hatası modele genel bir mesaj olarak
    /// döner; Qdrant/Ollama ayrıntısı sızmaz. RAG içeriği SaveWorkoutPlan doğrulamasını veya ACSM guardrail'ini ETKİLEMEZ.
    /// </summary>
    public sealed class KnowledgePlugin
    {
        public const string PluginName = "Knowledge";

        // Türkçe karakterler \uXXXX olarak kaçırılmasın: hem token maliyeti hem okunabilirlik için (Groq TPM limiti).
        private static readonly JsonSerializerOptions ResultJson = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IFitnessKnowledgeSearchService _search;

        public KnowledgePlugin(IFitnessKnowledgeSearchService search) => _search = search;

        [KernelFunction, Description(
            "Searches FitTracker's curated GENERAL fitness knowledge base (progressive overload, strength, hypertrophy, recovery, " +
            "deload/fatigue, training frequency, warm-up, rest periods, technique/safety, returning after a break). " +
            "Returns up to 3 short source-backed passages. Contains NO data about the user; use Workout tools for the user's own workouts. " +
            "Use this only for general fitness knowledge, never to answer a personal history, performance, trend, plateau, or planned-workout question. " +
            "Treat returned passages as the complete evidence for source-backed claims: do not invent numbers, percentages, ranges, thresholds, or source attributions that are not explicitly supported by the passages.")]
        public async Task<string> SearchFitnessKnowledge(
            [Description("Short general fitness question or keywords, e.g. 'deload nedir' or 'plato aşma'. No personal data, user history, weights, trends, or planned workouts.")] string query,
            CancellationToken cancellationToken = default)
        {
            var result = await _search.SearchAsync(query, cancellationToken);
            return JsonSerializer.Serialize(ToToolResult(result), ResultJson);
        }

        // Kompakt sonuç: yalnızca cevap ve kaynak gösterimi için gereken alanlar.
        internal static ToolResult ToToolResult(KnowledgeSearchResult result) => result.Status switch
        {
            KnowledgeSearchStatus.Ok => new ToolResult(
                "ok",
                "General fitness knowledge, not the user's personal data.",
                null,
                result.Hits.Select(h => new ToolHit(h.Title, h.Text, h.SourceName, h.SourceUrl, h.SourceVersion, h.Score)).ToList()),
            KnowledgeSearchStatus.NoResults => new ToolResult(
                "no_results",
                "No relevant entry in the knowledge base. Do not present any claim as source-backed.",
                null,
                new List<ToolHit>()),
            KnowledgeSearchStatus.InvalidQuery => new ToolResult(
                "invalid_query", null, "Query must be a short, non-empty general fitness question.", new List<ToolHit>()),
            _ => new ToolResult(
                "unavailable", null, "Fitness knowledge search is temporarily unavailable.", new List<ToolHit>())
        };

        internal sealed record ToolResult(
            [property: JsonPropertyName("status")] string Status,
            [property: JsonPropertyName("note")] string? Note,
            [property: JsonPropertyName("error")] string? Error,
            [property: JsonPropertyName("results")] IReadOnlyList<ToolHit> Results);

        internal sealed record ToolHit(
            [property: JsonPropertyName("title")] string Title,
            [property: JsonPropertyName("text")] string Text,
            [property: JsonPropertyName("sourceName")] string SourceName,
            [property: JsonPropertyName("sourceUrl")] string SourceUrl,
            [property: JsonPropertyName("sourceVersion")] string SourceVersion,
            [property: JsonPropertyName("score")] double Score);
    }
}
