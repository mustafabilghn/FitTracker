using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace FitTrackr.API.RAG
{
    /// <summary>
    /// Lokal Ollama <c>/api/embed</c> endpoint'i ile embedding üretir. API key yoktur. Hatalar (bağlantı, zaman aşımı,
    /// boyut uyuşmazlığı) exception olarak yükselir; çağıranlar (arama/ingestion) bunu kontrollü şekilde ele alır.
    /// </summary>
    public sealed class OllamaFitnessEmbeddingService : IFitnessEmbeddingService
    {
        public const string HttpClientName = "FitBotOllamaEmbeddings";
        private const int BatchSize = 16;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EmbeddingModelProfile _profile;
        private readonly TimeSpan _queryTimeout;
        private readonly TimeSpan _documentTimeout;

        public OllamaFitnessEmbeddingService(IHttpClientFactory httpClientFactory, IOptions<RagOptions> options)
        {
            _httpClientFactory = httpClientFactory;
            var value = options.Value;
            _profile = EmbeddingModelCatalog.Resolve(value.EmbeddingModel, value.EmbeddingDimensions);
            _queryTimeout = TimeSpan.FromSeconds(Math.Max(1, value.TimeoutSeconds));
            _documentTimeout = TimeSpan.FromSeconds(Math.Max(1, value.IngestionTimeoutSeconds));
        }

        public string ModelId => _profile.Model;
        public int Dimensions => _profile.Dimensions;
        public string Fingerprint => $"ollama|{_profile.Model}|{_profile.Dimensions}|{_profile.DocumentPrefix}";

        public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            var vectors = new List<ReadOnlyMemory<float>>(texts.Count);
            foreach (var batch in texts.Chunk(BatchSize))
                vectors.AddRange(await EmbedAsync(batch.Select(t => _profile.DocumentPrefix + t).ToArray(), _documentTimeout, cancellationToken));
            return vectors;
        }

        public async Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default) =>
            (await EmbedAsync(new[] { _profile.QueryPrefix + query }, _queryTimeout, cancellationToken))[0];

        // Zaman aşımı çağrı başınadır: chat sorgusu kısa (Rag:TimeoutSeconds), ingestion batch'i uzun (Rag:IngestionTimeoutSeconds).
        private async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(string[] inputs, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (Dimensions <= 0)
                throw new InvalidOperationException(
                    $"Embedding dimensions are unknown for model '{ModelId}'. Set Rag:EmbeddingDimensions.");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync("api/embed",
                new EmbedRequest(ModelId, inputs, Truncate: true), timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<EmbedResponse>(timeoutCts.Token)
                       ?? throw new InvalidOperationException("Empty embedding response.");
            var embeddings = body.Embeddings ?? new List<float[]>();
            if (embeddings.Count != inputs.Length)
                throw new InvalidOperationException($"Expected {inputs.Length} embeddings, got {embeddings.Count}.");

            foreach (var vector in embeddings)
                if (vector.Length != Dimensions)
                    throw new InvalidOperationException(
                        $"Embedding dimension mismatch for model '{ModelId}': expected {Dimensions}, got {vector.Length}.");

            return embeddings.Select(v => new ReadOnlyMemory<float>(v)).ToList();
        }

        private sealed record EmbedRequest(
            [property: JsonPropertyName("model")] string Model,
            [property: JsonPropertyName("input")] string[] Input,
            [property: JsonPropertyName("truncate")] bool Truncate);

        private sealed class EmbedResponse
        {
            [JsonPropertyName("embeddings")]
            public List<float[]>? Embeddings { get; set; }
        }
    }
}
