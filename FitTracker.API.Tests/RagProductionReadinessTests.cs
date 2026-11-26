using System.Net;
using System.Text;
using System.Text.Json;
using CommunityToolkit.VectorData.InMemory;
using FitTrackr.API.Plugins;
using FitTrackr.API.RAG;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Qdrant.Client;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// Production hazırlığı: embedding sağlayıcı seçimi (Ollama korunur, Azure OpenAI alternatifi), Qdrant API key,
/// koleksiyon boyut tutarlılığı, ingestion idempotency ve health check. Ağ, Qdrant veya gerçek Azure çağrısı yoktur.
/// </summary>
public class RagProductionReadinessTests
{
    private const string FakeKey = "test-only-key-not-a-secret";

    // ───────────────────── Provider selection ─────────────────────

    [Fact]
    public void ProviderSelection_DefaultIsOllama_AzureOpenAiIsOptIn_UnknownIsSafelyUnavailable()
    {
        Assert.IsType<OllamaFitnessEmbeddingService>(Resolve(new()).GetRequiredService<IFitnessEmbeddingService>());

        var azure = Resolve(new()
        {
            ["Rag:EmbeddingProvider"] = "AzureOpenAI",
            ["Rag:EmbeddingModel"] = "text-embedding-3-small",
            ["Rag:EmbeddingEndpoint"] = "https://example.openai.azure.com"
        }).GetRequiredService<IFitnessEmbeddingService>();
        Assert.IsType<AzureOpenAiFitnessEmbeddingService>(azure);
        Assert.Equal(1536, azure.Dimensions);
        Assert.StartsWith("azure-openai|text-embedding-3-small|1536", azure.Fingerprint);

        var unknown = Resolve(new() { ["Rag:EmbeddingProvider"] = "Typo" }).GetRequiredService<IFitnessEmbeddingService>();
        Assert.IsType<MisconfiguredFitnessEmbeddingService>(unknown);
        Assert.Equal(0, unknown.Dimensions);
    }

    [Fact]
    public async Task UnknownProvider_DoesNotBreakTheApp_SearchIsUnavailable()
    {
        using var provider = Resolve(new() { ["Rag:EmbeddingProvider"] = "Typo" });
        var result = await provider.GetRequiredService<IFitnessKnowledgeSearchService>().SearchAsync("deload nedir");
        Assert.Equal(KnowledgeSearchStatus.Unavailable, result.Status);
    }

    [Fact]
    public void Catalog_KeepsNomicProfile_AndAddsHostedModelsWithoutPrefixes()
    {
        var nomic = EmbeddingModelCatalog.Resolve("nomic-embed-text-v2-moe", null);
        Assert.Equal(768, nomic.Dimensions);
        Assert.Equal("search_query: ", nomic.QueryPrefix);

        var small = EmbeddingModelCatalog.Resolve("text-embedding-3-small", null);
        Assert.Equal(1536, small.Dimensions);
        Assert.Equal(string.Empty, small.QueryPrefix);
        Assert.Equal(string.Empty, small.DocumentPrefix);
    }

    [Fact]
    public void QdrantClient_IsCreatedLazily_WithOptionalApiKey_AndSharedByStoreAndInspector()
    {
        using var provider = Resolve(new()
        {
            ["Rag:QdrantEndpoint"] = "https://example.cloud.qdrant.io:6333",
            ["Rag:QdrantApiKey"] = FakeKey
        });

        // Çözümleme ağa çıkmaz; key'li ve https istemci oluşturulabilir.
        var client = provider.GetRequiredKeyedService<QdrantClient>(RagServiceKeys.VectorStore);
        Assert.NotNull(client);
        Assert.NotNull(provider.GetRequiredKeyedService<VectorStore>(RagServiceKeys.VectorStore));
        Assert.IsType<QdrantKnowledgeCollectionSchemaInspector>(provider.GetRequiredService<IKnowledgeCollectionSchemaInspector>());
    }

    // ───────────────────── Azure OpenAI embedding provider ─────────────────────

    [Fact]
    public async Task AzureOpenAi_CallsDeploymentEndpoint_WithApiKeyHeader_AndOrdersByIndex()
    {
        var handler = new AzureStubHandler(dimensions: 1536, reverseOrder: true);
        var service = CreateAzure(handler, new RagOptions
        {
            EmbeddingProvider = "AzureOpenAI",
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingDeployment = "fitbot-embeddings",
            EmbeddingApiKey = FakeKey
        });

        var query = await service.EmbedQueryAsync("deload nedir");
        var docs = await service.EmbedDocumentsAsync(new[] { "a", "b", "c" });

        Assert.Equal(1536, query.Length);
        Assert.Equal(3, docs.Count);
        Assert.Equal(0f, docs[0].Span[0]); // stub vector[0] = index → sıralama index'e göre düzeltildi
        Assert.Equal(2f, docs[2].Span[0]);

        var first = handler.Requests[0];
        Assert.Equal("/openai/deployments/fitbot-embeddings/embeddings", first.Path);
        Assert.Contains("api-version=2024-10-21", first.Query);
        Assert.Equal(FakeKey, first.ApiKey);
        Assert.False(first.HasAuthorizationHeader);
        using var body = JsonDocument.Parse(first.Body);
        Assert.Equal("deload nedir", body.RootElement.GetProperty("input")[0].GetString()); // ön ek yok
        Assert.False(body.RootElement.TryGetProperty("dimensions", out _)); // açıkça yapılandırılmadıkça gönderilmez
    }

    [Fact]
    public async Task AzureOpenAi_FailsClosed_OnMissingKey_DimensionMismatch_AndHttpError_WithoutLeakingSecrets()
    {
        var noKey = CreateAzure(new AzureStubHandler(1536), new RagOptions { EmbeddingModel = "text-embedding-3-small" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => noKey.EmbedQueryAsync("x"));

        var mismatch = CreateAzure(new AzureStubHandler(768), new RagOptions { EmbeddingModel = "text-embedding-3-small", EmbeddingApiKey = FakeKey });
        await Assert.ThrowsAsync<InvalidOperationException>(() => mismatch.EmbedQueryAsync("x"));

        var failing = CreateAzure(new AzureStubHandler(1536, status: HttpStatusCode.Unauthorized),
            new RagOptions { EmbeddingModel = "text-embedding-3-small", EmbeddingApiKey = FakeKey });
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => failing.EmbedQueryAsync("x"));
        Assert.DoesNotContain(FakeKey, ex.Message);
        Assert.DoesNotContain("internal-detail", ex.Message);
    }

    [Fact]
    public async Task AzureOpenAi_RetriesOnceOnTransientError_ButNotOnPermanentError()
    {
        var transient = new SequenceHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var ok = CreateAzure(transient, new RagOptions { EmbeddingModel = "text-embedding-3-small", EmbeddingApiKey = FakeKey });
        Assert.Equal(1536, (await ok.EmbedQueryAsync("deload")).Length);
        Assert.Equal(2, transient.Calls);

        var stillFailing = new SequenceHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var bounded = CreateAzure(stillFailing, new RagOptions { EmbeddingModel = "text-embedding-3-small", EmbeddingApiKey = FakeKey });
        await Assert.ThrowsAsync<HttpRequestException>(() => bounded.EmbedQueryAsync("deload"));
        Assert.Equal(2, stillFailing.Calls); // en fazla 1 ek deneme

        var permanent = new SequenceHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        var noRetry = CreateAzure(permanent, new RagOptions { EmbeddingModel = "text-embedding-3-small", EmbeddingApiKey = FakeKey });
        await Assert.ThrowsAsync<HttpRequestException>(() => noRetry.EmbedQueryAsync("deload"));
        Assert.Equal(1, permanent.Calls);
    }

    [Theory]
    [InlineData("", "https://example.openai.azure.com")]
    [InlineData("https://x.cloud.qdrant.io:6333", "")]
    [InlineData("not-a-uri", "https://example.openai.azure.com")]
    public void EnabledButMissingEndpoints_RegistersNothing_SoChatStaysNonRag(string qdrant, string embedding)
    {
        var services = new ServiceCollection();
        services.AddFitBotKnowledgePlugin(Config(new() { ["Rag:QdrantEndpoint"] = qdrant, ["Rag:EmbeddingEndpoint"] = embedding }));

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(KnowledgePlugin));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IFitnessEmbeddingService));
    }

    [Fact]
    public void ProductionSettingsFile_KeepsRagOff_AndHasNoLocalhostDependency()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.Production.json");
        Assert.True(File.Exists(path), "appsettings.Production.json must be published with the API");
        var rag = new ConfigurationBuilder().AddJsonFile(path).Build().GetSection("Rag");

        Assert.Equal("false", rag["Enabled"], ignoreCase: true);
        Assert.DoesNotContain("localhost", rag["QdrantEndpoint"] ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", rag["EmbeddingEndpoint"] ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrEmpty(rag["QdrantApiKey"]));      // secret'lar dosyada değil
        Assert.True(string.IsNullOrEmpty(rag["EmbeddingApiKey"]));
        Assert.NotEqual("fittracker_fitness_knowledge", rag["CollectionName"]); // yeni model → ayrı koleksiyon
    }

    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Calls, statuses.Length - 1)];
            Calls++;
            if (status != HttpStatusCode.OK)
                return new HttpResponseMessage(status);
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var count = doc.RootElement.GetProperty("input").GetArrayLength();
            var payload = JsonSerializer.Serialize(new { data = Enumerable.Range(0, count).Select(i => new { index = i, embedding = Enumerable.Repeat(0.1f, 1536).ToArray() }).ToArray() });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task AzureOpenAi_SendsDimensions_OnlyWhenExplicitlyConfigured()
    {
        var handler = new AzureStubHandler(512);
        var service = CreateAzure(handler, new RagOptions { EmbeddingModel = "text-embedding-3-small", EmbeddingDimensions = 512, EmbeddingApiKey = FakeKey });

        await service.EmbedQueryAsync("x");

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(512, body.RootElement.GetProperty("dimensions").GetInt32());
        Assert.Equal(512, service.Dimensions);
    }

    // ───────────────────── Collection dimension consistency ─────────────────────

    [Fact]
    public async Task Ingestion_RefusesToWrite_WhenExistingCollectionHasDifferentVectorSize()
    {
        using var h = FitnessKnowledgeRagTests.RagHarness.Create();
        var ingestion = new KnowledgeIngestionService(
            h.Corpus, h.Embeddings, h.Provider.GetRequiredService<FitnessKnowledgeCollectionProvider>(),
            NullLogger<KnowledgeIngestionService>.Instance, new FixedInspector(768)); // embedding: 1024

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ingestion.IngestAsync());
        Assert.Contains("Rag:CollectionName", ex.Message);
        Assert.Equal(0, h.Embeddings.DocumentCalls); // hiçbir embedding/yazma yapılmadı
        Assert.False(await h.Provider.GetRequiredService<FitnessKnowledgeCollectionProvider>().Get().CollectionExistsAsync());
    }

    [Fact]
    public async Task Ingestion_IsIdempotent_WhenCollectionMatches_SecondRunEmbedsNothing()
    {
        using var h = FitnessKnowledgeRagTests.RagHarness.Create();
        var ingestion = new KnowledgeIngestionService(
            h.Corpus, h.Embeddings, h.Provider.GetRequiredService<FitnessKnowledgeCollectionProvider>(),
            NullLogger<KnowledgeIngestionService>.Instance, new FixedInspector(HashingEmbeddingService.Dims));

        var first = await ingestion.IngestAsync();
        var embeddedAfterFirst = h.Embeddings.EmbeddedDocuments;
        var second = await ingestion.IngestAsync();

        Assert.Equal(first.TotalItems, first.Embedded);
        Assert.Equal(0, second.Embedded);
        Assert.Equal(second.TotalItems, second.Unchanged);
        Assert.Equal(embeddedAfterFirst, h.Embeddings.EmbeddedDocuments);
        Assert.Equal(first.CorpusHash, second.CorpusHash);
    }

    // ───────────────────── Health check ─────────────────────

    [Theory]
    [InlineData(false, null, "Healthy")]   // RAG kapalı
    [InlineData(true, null, "Degraded")]   // koleksiyon yok (ingestion bekliyor)
    [InlineData(true, 768, "Degraded")]    // boyut uyuşmazlığı
    [InlineData(true, HashingEmbeddingService.Dims, "Healthy")]
    public async Task HealthCheck_ReportsRagState_WithoutCallingEmbeddingService(bool enabled, int? collectionDims, string expected)
    {
        var embeddings = new HashingEmbeddingService();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFitnessEmbeddingService>(embeddings);
        services.AddKeyedSingleton<VectorStore>(RagServiceKeys.VectorStore, new InMemoryVectorStore());
        services.AddSingleton<IKnowledgeCollectionSchemaInspector>(new FixedInspector(collectionDims));
        services.AddFitBotKnowledgePlugin(Config(new() { ["Rag:Enabled"] = enabled ? "true" : "false" }));
        using var provider = services.BuildServiceProvider();

        var check = ActivatorUtilities.CreateInstance<RagHealthCheck>(provider);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(expected, result.Status.ToString());
        Assert.Empty(embeddings.Queries);
    }

    [Fact]
    public async Task HealthCheck_IsDegraded_NotUnhealthy_WhenVectorStoreIsUnreachable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFitnessEmbeddingService>(new HashingEmbeddingService());
        services.AddKeyedSingleton<VectorStore>(RagServiceKeys.VectorStore, new InMemoryVectorStore());
        services.AddSingleton<IKnowledgeCollectionSchemaInspector>(new ThrowingInspector());
        services.AddFitBotKnowledgePlugin(Config(new() { ["Rag:Enabled"] = "true" }));
        using var provider = services.BuildServiceProvider();

        var result = await ActivatorUtilities.CreateInstance<RagHealthCheck>(provider).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.DoesNotContain("SECRET", result.Description);
    }

    // ───────────────────── Helpers ─────────────────────

    private static IConfiguration Config(Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Rag:Enabled"] = "true",
            ["Rag:QdrantEndpoint"] = "http://127.0.0.1:9",
            ["Rag:EmbeddingEndpoint"] = "http://127.0.0.1:9",
            ["Rag:TimeoutSeconds"] = "2"
        };
        foreach (var (k, v) in overrides)
            values[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceProvider Resolve(Dictionary<string, string?> overrides)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFitBotKnowledgePlugin(Config(overrides));
        return services.BuildServiceProvider();
    }

    private static AzureOpenAiFitnessEmbeddingService CreateAzure(HttpMessageHandler handler, RagOptions options)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(AzureOpenAiFitnessEmbeddingService.HttpClientName, c => c.BaseAddress = new Uri("https://example.openai.azure.com/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return new AzureOpenAiFitnessEmbeddingService(factory, Options.Create(options));
    }

    private sealed record AzureRequest(string Path, string Query, string? ApiKey, bool HasAuthorizationHeader, string Body);

    private sealed class AzureStubHandler(int dimensions, bool reverseOrder = false, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<AzureRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add(new AzureRequest(request.RequestUri!.AbsolutePath, request.RequestUri.Query,
                request.Headers.TryGetValues("api-key", out var keys) ? keys.Single() : null,
                request.Headers.Authorization is not null, body));

            if (status != HttpStatusCode.OK)
                return new HttpResponseMessage(status) { Content = new StringContent("{\"error\":\"internal-detail\"}") };

            using var doc = JsonDocument.Parse(body);
            var count = doc.RootElement.GetProperty("input").GetArrayLength();
            var items = Enumerable.Range(0, count)
                .Select(i => new { index = i, embedding = Enumerable.Range(0, dimensions).Select(j => j == 0 ? (float)i : 0.1f).ToArray() });
            if (reverseOrder)
                items = items.Reverse();
            var payload = JsonSerializer.Serialize(new { data = items.ToArray() });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FixedInspector(int? dims) : IKnowledgeCollectionSchemaInspector
    {
        public Task<int?> GetVectorDimensionsAsync(string collectionName, CancellationToken cancellationToken = default) => Task.FromResult(dims);
    }

    private sealed class ThrowingInspector : IKnowledgeCollectionSchemaInspector
    {
        public Task<int?> GetVectorDimensionsAsync(string collectionName, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("qdrant.internal:6334 SECRET");
    }
}
