using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FitTrackr.API.RAG
{
    /// <summary>
    /// Açılışta (varsayılan: yalnızca Development) bilgi tabanını ARKA PLANDA ingest eder. Uygulamanın açılmasını
    /// beklemez; Qdrant/Ollama erişilemiyorsa yalnızca uyarı loglar, Chat endpoint'i çalışmaya devam eder (RAG tool'u
    /// bu durumda "unavailable" döner). Corpus değişmediyse ingestion hiç embedding üretmez.
    /// </summary>
    public sealed class KnowledgeIngestionHostedService : BackgroundService
    {
        private readonly IKnowledgeIngestionService _ingestion;
        private readonly RagOptions _options;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<KnowledgeIngestionHostedService> _logger;

        public KnowledgeIngestionHostedService(
            IKnowledgeIngestionService ingestion,
            IOptions<RagOptions> options,
            IHostEnvironment environment,
            ILogger<KnowledgeIngestionHostedService> logger)
        {
            _ingestion = ingestion;
            _options = options.Value;
            _environment = environment;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var ingestOnStartup = _options.IngestOnStartup ?? _environment.IsDevelopment();
            if (_options.Enabled)
            {
                // Efektif yapılandırma özeti (secret İÇERMEZ: key'ler yalnızca "set/not set" olarak raporlanır).
                var qdrantHost = Uri.TryCreate(_options.QdrantEndpoint, UriKind.Absolute, out var q) ? $"{q.Scheme}://{q.Host}" : "invalid";
                _logger.LogInformation(
                    "FitBot RAG enabled: provider={Provider} model={Model} collection={Collection} qdrant={QdrantHost} qdrantApiKey={QdrantKeySet} embeddingApiKey={EmbeddingKeySet} topK={TopK} threshold={Threshold} ingestOnStartup={IngestOnStartup}",
                    _options.EmbeddingProvider, _options.EmbeddingModel, _options.CollectionName, qdrantHost,
                    string.IsNullOrWhiteSpace(_options.QdrantApiKey) ? "not set" : "set",
                    string.IsNullOrWhiteSpace(_options.EmbeddingApiKey) ? "not set" : "set",
                    _options.EffectiveTopK, _options.MinRelevanceScore, ingestOnStartup);
            }

            if (!_options.Enabled || !ingestOnStartup)
                return;

            await Task.Yield(); // host açılışını bloklama

            try
            {
                await _ingestion.IngestAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "FitBot knowledge ingestion skipped: knowledge backend unavailable ({ErrorType}). Chat keeps working without RAG results.",
                    ex.GetType().Name);
            }
        }
    }
}
