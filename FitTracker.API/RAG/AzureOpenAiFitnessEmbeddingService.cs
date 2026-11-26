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
    /// Azure OpenAI embeddings (<c>POST {endpoint}/openai/deployments/{deployment}/embeddings?api-version=...</c>).
    /// Ollama implementasyonunun alternatifidir; <c>Rag:EmbeddingProvider=AzureOpenAI</c> ile seçilir. Ek paket gerektirmez.
    /// API key yalnızca <c>api-key</c> header'ında gönderilir; hata mesajlarına veya loglara yazılmaz.
    /// Hatalar exception olarak yükselir; arama servisi bunları kontrollü "unavailable" sonucuna çevirir.
    /// </summary>
    public sealed class AzureOpenAiFitnessEmbeddingService : IFitnessEmbeddingService
    {
        public const string HttpClientName = "FitBotAzureOpenAiEmbeddings";
        private const int BatchSize = 16;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EmbeddingModelProfile _profile;
        private readonly string _deployment;
        private readonly string _apiVersion;
        private readonly string? _apiKey;
        private readonly int? _requestedDimensions;
        private readonly TimeSpan _queryTimeout;
        private readonly TimeSpan _documentTimeout;

        public AzureOpenAiFitnessEmbeddingService(IHttpClientFactory httpClientFactory, IOptions<RagOptions> options)
        {
            _httpClientFactory = httpClientFactory;
            var value = options.Value;
            _profile = EmbeddingModelCatalog.Resolve(value.EmbeddingModel, value.EmbeddingDimensions);
            _deployment = string.IsNullOrWhiteSpace(value.EmbeddingDeployment) ? _profile.Model : value.EmbeddingDeployment.Trim();
            _apiVersion = string.IsNullOrWhiteSpace(value.EmbeddingApiVersion) ? "2024-10-21" : value.EmbeddingApiVersion.Trim();
            _apiKey = value.EmbeddingApiKey;
            // Boyut yalnızca açıkça yapılandırıldıysa istenir (text-embedding-3 modelleri boyut kısaltmayı destekler).
            _requestedDimensions = value.EmbeddingDimensions is > 0 ? value.EmbeddingDimensions : null;
            _queryTimeout = TimeSpan.FromSeconds(Math.Max(1, value.TimeoutSeconds));
            _documentTimeout = TimeSpan.FromSeconds(Math.Max(1, value.IngestionTimeoutSeconds));
        }

        public string ModelId => _profile.Model;
        public int Dimensions => _profile.Dimensions;
        public string Fingerprint => $"azure-openai|{_profile.Model}|{_profile.Dimensions}|{_profile.DocumentPrefix}";

        public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            var vectors = new List<ReadOnlyMemory<float>>(texts.Count);
            foreach (var batch in texts.Chunk(BatchSize))
                vectors.AddRange(await EmbedAsync(batch.Select(t => _profile.DocumentPrefix + t).ToArray(), _documentTimeout, cancellationToken));
            return vectors;
        }

        public async Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default) =>
            (await EmbedAsync(new[] { _profile.QueryPrefix + query }, _queryTimeout, cancellationToken))[0];

        private async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(string[] inputs, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (Dimensions <= 0)
                throw new InvalidOperationException(
                    $"Embedding dimensions are unknown for model '{ModelId}'. Set Rag:EmbeddingDimensions.");
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("Rag:EmbeddingApiKey is not configured for the AzureOpenAI embedding provider.");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await SendWithTransientRetryAsync(client, inputs, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
                // Gövde (olası iç ayrıntı) bilerek eklenmez; yalnızca durum kodu.
                throw new HttpRequestException($"Azure OpenAI embedding request failed ({(int)response.StatusCode}).", null, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<EmbedResponse>(timeoutCts.Token)
                       ?? throw new InvalidOperationException("Empty embedding response.");
            var data = (body.Data ?? new List<EmbedItem>()).OrderBy(d => d.Index).ToList();
            if (data.Count != inputs.Length)
                throw new InvalidOperationException($"Expected {inputs.Length} embeddings, got {data.Count}.");

            foreach (var item in data)
                if ((item.Embedding?.Length ?? 0) != Dimensions)
                    throw new InvalidOperationException(
                        $"Embedding dimension mismatch for model '{ModelId}': expected {Dimensions}, got {item.Embedding?.Length ?? 0}.");

            return data.Select(d => new ReadOnlyMemory<float>(d.Embedding!)).ToList();
        }

        /// <summary>En fazla bu kadar ek deneme (yalnızca geçici hatalar: ağ hatası, 408, 429, 5xx).</summary>
        public const int MaxTransientRetries = 1;
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(2);

        // Tek bir kısa yeniden deneme: toplam süre çağrının zaman aşımıyla (sorgu: Rag:TimeoutSeconds) sınırlıdır; kalıcı
        // hatalar (401/403/404/400) tekrar denenmez. Başarısızlıkta arama servisi "unavailable" + cooldown'a düşer (fallback).
        private async Task<HttpResponseMessage> SendWithTransientRetryAsync(HttpClient client, string[] inputs, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    $"openai/deployments/{Uri.EscapeDataString(_deployment)}/embeddings?api-version={Uri.EscapeDataString(_apiVersion)}")
                {
                    Content = JsonContent.Create(new EmbedRequest(inputs, _requestedDimensions))
                };
                request.Headers.Add("api-key", _apiKey);

                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(request, cancellationToken);
                }
                catch (HttpRequestException) when (attempt < MaxTransientRetries && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                    continue;
                }

                var transient = (int)response.StatusCode is 408 or 429 or >= 500;
                if (!transient || attempt >= MaxTransientRetries)
                    return response;

                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(500);
                response.Dispose();
                await Task.Delay(delay > MaxRetryDelay ? MaxRetryDelay : delay, cancellationToken);
            }
        }

        private sealed record EmbedRequest(
            [property: JsonPropertyName("input")] string[] Input,
            [property: JsonPropertyName("dimensions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Dimensions);

        private sealed class EmbedResponse
        {
            [JsonPropertyName("data")]
            public List<EmbedItem>? Data { get; set; }
        }

        private sealed class EmbedItem
        {
            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("embedding")]
            public float[]? Embedding { get; set; }
        }
    }

    /// <summary>
    /// Bilinmeyen/yanlış yapılandırılmış sağlayıcı için yer tutucu: uygulama açılır, Chat çalışır; RAG kontrollü olarak
    /// "unavailable" olur (Dimensions = 0). Yanlış bir ayar API'yi düşürmez.
    /// </summary>
    public sealed class MisconfiguredFitnessEmbeddingService : IFitnessEmbeddingService
    {
        private readonly string _provider;

        public MisconfiguredFitnessEmbeddingService(IOptions<RagOptions> options) => _provider = options.Value.EmbeddingProvider;

        public string ModelId => $"unsupported-provider:{_provider}";
        public int Dimensions => 0;
        public string Fingerprint => $"unsupported|{_provider}";

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"Unsupported Rag:EmbeddingProvider '{_provider}'.");

        public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"Unsupported Rag:EmbeddingProvider '{_provider}'.");
    }
}
