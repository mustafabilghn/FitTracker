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
            if (!_options.Enabled || !(_options.IngestOnStartup ?? _environment.IsDevelopment()))
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
