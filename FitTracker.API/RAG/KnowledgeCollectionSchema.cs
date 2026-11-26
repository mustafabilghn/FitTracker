using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace FitTrackr.API.RAG
{
    /// <summary>
    /// Vector store'daki koleksiyonun gerçek vektör boyutunu okur. Embedding modeli değiştiğinde (ör. nomic 768 →
    /// text-embedding-3-small 1536) eski koleksiyona yazılmasını/aranmasını açık bir hatayla engellemek için kullanılır.
    /// </summary>
    public interface IKnowledgeCollectionSchemaInspector
    {
        /// <summary>Koleksiyon yoksa veya boyut belirlenemiyorsa <c>null</c>.</summary>
        Task<int?> GetVectorDimensionsAsync(string collectionName, CancellationToken cancellationToken = default);
    }

    /// <summary>Qdrant implementasyonu (gRPC collection info).</summary>
    public sealed class QdrantKnowledgeCollectionSchemaInspector : IKnowledgeCollectionSchemaInspector
    {
        private readonly QdrantClient _client;

        public QdrantKnowledgeCollectionSchemaInspector([FromKeyedServices(RagServiceKeys.VectorStore)] QdrantClient client) => _client = client;

        public async Task<int?> GetVectorDimensionsAsync(string collectionName, CancellationToken cancellationToken = default)
        {
            if (!await _client.CollectionExistsAsync(collectionName, cancellationToken))
                return null;

            var info = await _client.GetCollectionInfoAsync(collectionName, cancellationToken);
            var vectors = info.Config?.Params?.VectorsConfig;
            if (vectors is null)
                return null;

            if (vectors.ConfigCase == VectorsConfig.ConfigOneofCase.Params)
                return (int)vectors.Params.Size;

            if (vectors.ConfigCase == VectorsConfig.ConfigOneofCase.ParamsMap && vectors.ParamsMap.Map.Count > 0)
            {
                var map = vectors.ParamsMap.Map;
                return (int)(map.TryGetValue(nameof(FitnessKnowledgeRecord.Embedding), out var named) ? named : map.Values.First()).Size;
            }

            return null;
        }
    }

    public static class KnowledgeCollectionSchema
    {
        public static void EnsureCompatible(string collectionName, int? actualDimensions, IFitnessEmbeddingService embeddings)
        {
            if (actualDimensions is { } actual && actual != embeddings.Dimensions)
                throw new InvalidOperationException(
                    $"Knowledge collection '{collectionName}' has vector size {actual}, but embedding model '{embeddings.ModelId}' " +
                    $"produces {embeddings.Dimensions}. Use a new Rag:CollectionName for this model and re-run ingestion.");
        }
    }

    /// <summary>
    /// Hafif RAG health check: embedding servisine istek ATMAZ (maliyet/kota yok). RAG kapalıysa Healthy("disabled").
    /// Sorunlarda <see cref="HealthStatus.Degraded"/> döner (Chat RAG'siz çalışmaya devam ettiği için Unhealthy değil);
    /// yanıt gövdesi yalnızca durum metnidir, iç ayrıntı (host, key, koleksiyon) dışarı verilmez.
    /// </summary>
    public sealed class RagHealthCheck : IHealthCheck
    {
        private readonly IServiceProvider _services;
        private readonly RagOptions _options;

        public RagHealthCheck(IServiceProvider services, IOptions<RagOptions> options)
        {
            _services = services;
            _options = options.Value;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
                return HealthCheckResult.Healthy("rag disabled");

            var embeddings = _services.GetService<IFitnessEmbeddingService>();
            if (embeddings is null || embeddings.Dimensions <= 0)
                return HealthCheckResult.Degraded("embedding provider misconfigured");

            if (_services.GetService<KnowledgeAvailability>()?.IsCoolingDown == true)
                return HealthCheckResult.Degraded("knowledge backend recently unavailable");

            var inspector = _services.GetService<IKnowledgeCollectionSchemaInspector>();
            if (inspector is null)
                return HealthCheckResult.Healthy("rag enabled");

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
                var dims = await inspector.GetVectorDimensionsAsync(_options.CollectionName, timeout.Token);
                if (dims is null)
                    return HealthCheckResult.Degraded("knowledge collection missing (ingestion pending)");
                if (dims != embeddings.Dimensions)
                    return HealthCheckResult.Degraded("knowledge collection dimension mismatch");
                return HealthCheckResult.Healthy("rag ready");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Degraded("vector store unreachable");
            }
        }
    }
}
