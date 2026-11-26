using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;

namespace FitTrackr.API.RAG
{
    public enum KnowledgeSearchStatus
    {
        Ok,
        NoResults,
        InvalidQuery,
        Unavailable
    }

    public sealed record KnowledgeSearchHit(
        string ChunkId,
        string Title,
        string Text,
        string Category,
        string Language,
        string Authority,
        string SourceName,
        string SourceUrl,
        string SourceVersion,
        double Score);

    public sealed record KnowledgeSearchResult(KnowledgeSearchStatus Status, IReadOnlyList<KnowledgeSearchHit> Hits)
    {
        public static KnowledgeSearchResult Of(KnowledgeSearchStatus status) => new(status, Array.Empty<KnowledgeSearchHit>());
    }

    public interface IFitnessKnowledgeSearchService
    {
        /// <summary>
        /// Sorguyu embed edip benzerlik araması yapar; en fazla <see cref="RagOptions.MaxTopK"/> sonuç ve yalnızca
        /// <see cref="RagOptions.MinRelevanceScore"/> üstü skorlar döner. Backend hatası exception değil
        /// <see cref="KnowledgeSearchStatus.Unavailable"/> olarak döner (chat'i bozmaz).
        /// </summary>
        Task<KnowledgeSearchResult> SearchAsync(string query, CancellationToken cancellationToken = default);
    }

    public sealed class FitnessKnowledgeSearchService : IFitnessKnowledgeSearchService
    {
        public const int MaxQueryLength = 300;

        private readonly IFitnessEmbeddingService _embeddings;
        private readonly FitnessKnowledgeCollectionProvider _collectionProvider;
        private readonly KnowledgeAvailability _availability;
        private readonly RagOptions _options;
        private readonly ILogger<FitnessKnowledgeSearchService> _logger;

        public FitnessKnowledgeSearchService(
            IFitnessEmbeddingService embeddings,
            FitnessKnowledgeCollectionProvider collectionProvider,
            KnowledgeAvailability availability,
            IOptions<RagOptions> options,
            ILogger<FitnessKnowledgeSearchService> logger)
        {
            _embeddings = embeddings;
            _collectionProvider = collectionProvider;
            _availability = availability;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<KnowledgeSearchResult> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            var normalized = Normalize(query);
            if (normalized.Length == 0)
                return KnowledgeSearchResult.Of(KnowledgeSearchStatus.InvalidQuery);

            if (_availability.IsCoolingDown)
            {
                LogTelemetry(KnowledgeSearchStatus.Unavailable, 0, null, 0, coolingDown: true);
                return KnowledgeSearchResult.Of(KnowledgeSearchStatus.Unavailable);
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var vector = await _embeddings.EmbedQueryAsync(normalized, cancellationToken);
                var collection = _collectionProvider.Get();
                var topK = _options.EffectiveTopK;

                var hits = new List<KnowledgeSearchHit>();
                await foreach (var result in collection.SearchAsync(vector, topK,
                                   new VectorSearchOptions<FitnessKnowledgeRecord> { IncludeVectors = false }, cancellationToken))
                {
                    var score = result.Score ?? 0;
                    if (score < _options.MinRelevanceScore)
                        continue;

                    var r = result.Record;
                    hits.Add(new KnowledgeSearchHit(r.ChunkId, r.Title, r.Text, r.Category, r.Language, r.Authority,
                        r.SourceName, r.SourceUrl, r.SourceVersion, Math.Round(score, 3)));
                }

                _availability.MarkAvailable();
                var ordered = hits.OrderByDescending(h => h.Score).Take(topK).ToList();
                var status = ordered.Count == 0 ? KnowledgeSearchStatus.NoResults : KnowledgeSearchStatus.Ok;
                LogTelemetry(status, ordered.Count, ordered.Count == 0 ? null : ordered[0].Score, stopwatch.ElapsedMilliseconds, coolingDown: false);
                return status == KnowledgeSearchStatus.NoResults
                    ? KnowledgeSearchResult.Of(KnowledgeSearchStatus.NoResults)
                    : new KnowledgeSearchResult(KnowledgeSearchStatus.Ok, ordered);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Ayrıntı yalnızca sunucu loguna gider; modele/kullanıcıya genel "unavailable" döner.
                _availability.MarkUnavailable(TimeSpan.FromSeconds(Math.Max(0, _options.UnavailableCooldownSeconds)));
                _logger.LogWarning(ex, "FitBot knowledge search unavailable ({ErrorType}).", ex.GetType().Name);
                LogTelemetry(KnowledgeSearchStatus.Unavailable, 0, null, stopwatch.ElapsedMilliseconds, coolingDown: false);
                return KnowledgeSearchResult.Of(KnowledgeSearchStatus.Unavailable);
            }
        }

        // Temel RAG telemetrisi: durum, sonuç sayısı, en yüksek skor, süre, model. Sorgu metni / kullanıcı verisi LOGLANMAZ.
        private void LogTelemetry(KnowledgeSearchStatus status, int hits, double? topScore, long elapsedMs, bool coolingDown) =>
            _logger.LogInformation(
                "FitBot knowledge search: status={Status} hits={Hits} topScore={TopScore} elapsedMs={ElapsedMs} coolingDown={CoolingDown} model={Model} threshold={Threshold}",
                status, hits, topScore, elapsedMs, coolingDown, _embeddings.ModelId, _options.MinRelevanceScore);

        public static string Normalize(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;
            var collapsed = Regex.Replace(query, @"\s+", " ").Trim();
            return collapsed.Length <= MaxQueryLength ? collapsed : collapsed[..MaxQueryLength];
        }
    }

    /// <summary>
    /// Basit devre kesici: backend hatasından sonra kısa bir süre arama denenmez; erişilemeyen Ollama/Qdrant her chat
    /// isteğini zaman aşımı kadar bekletmez.
    /// </summary>
    public sealed class KnowledgeAvailability
    {
        private long _unavailableUntilTicks;

        public bool IsCoolingDown => DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref _unavailableUntilTicks);

        public void MarkUnavailable(TimeSpan cooldown) =>
            Interlocked.Exchange(ref _unavailableUntilTicks, DateTimeOffset.UtcNow.Add(cooldown).UtcTicks);

        public void MarkAvailable() => Interlocked.Exchange(ref _unavailableUntilTicks, 0);
    }
}
