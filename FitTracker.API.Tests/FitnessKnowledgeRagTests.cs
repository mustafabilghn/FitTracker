using System.Net;
using System.Text;
using System.Text.Json;
using CommunityToolkit.VectorData.InMemory;
using FitTrackr.API.Plugins;
using FitTrackr.API.RAG;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// FitBot bilgi tabanı (RAG) testleri: corpus, ingestion (yeniden embedding yok), koleksiyon/upsert, retrieval, eşik,
/// metadata, tool sözleşmesi ve hata yolu. Production DI kaydı (AddFitBotKnowledgePlugin) kullanılır; yalnızca vector store
/// (in-memory) ve embedding (deterministik sahte) değiştirilir — ağ, Qdrant veya Ollama gerekmez.
/// </summary>
public class FitnessKnowledgeRagTests
{
    // ───────────────────── 1. Corpus ─────────────────────

    [Fact]
    public void Corpus_LoadsFromEmbeddedResource_WithCompleteMetadata_AndControlledSize()
    {
        var corpus = FitnessKnowledgeCorpusLoader.LoadEmbedded();

        Assert.Equal("fittracker-fitness-knowledge", corpus.Corpus);
        Assert.False(string.IsNullOrWhiteSpace(corpus.Version));
        Assert.InRange(corpus.Items.Count, 15, 30);
        Assert.Equal(corpus.Items.Count, corpus.Items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var item in corpus.Items)
        {
            Assert.All(new[] { item.Id, item.Title, item.Text, item.Category, item.SourceName, item.SourceUrl, item.SourceVersion },
                v => Assert.False(string.IsNullOrWhiteSpace(v)));
            Assert.Equal("tr", item.Language);
            Assert.StartsWith("https://", item.SourceUrl);
            Assert.InRange(FitnessKnowledgeCorpusLoader.CountWords(item.Text), 50, 300);
        }

        // İstenen konuların hepsi kapsanıyor.
        var categories = corpus.Items.Select(i => i.Category).ToHashSet();
        foreach (var required in new[] { "resistance_training_fundamentals", "progressive_overload", "strength_training", "hypertrophy",
                                          "recovery", "deload_fatigue", "training_frequency", "warm_up", "rest_periods",
                                          "technique_safety", "return_to_training" })
            Assert.Contains(required, categories);

        // Güncel ACSM 2026 otoriter; eski 2009 önerisi ayrı tutulmuş ve "historical" olarak işaretli.
        Assert.Contains(corpus.Items, i => i.Authority == "current" && i.SourceVersion.Contains("2026;58(4)"));
        var historical = Assert.Single(corpus.Items, i => i.Authority == "historical");
        Assert.Contains("2009", historical.SourceVersion);
        Assert.All(corpus.Items.Where(i => i.Authority == "current"), i => Assert.DoesNotContain("2009;41", i.SourceVersion));
    }

    [Fact]
    public void Corpus_Validation_RejectsDuplicatesMissingMetadataAndOversizedChunks()
    {
        var valid = FitnessKnowledgeCorpusLoader.LoadEmbedded();

        var duplicate = MutableCorpusSource.Clone(valid);
        duplicate.Items.Add(MutableCorpusSource.Clone(valid).Items[0]);
        Assert.Contains("duplicate id", Assert.Throws<InvalidOperationException>(() => FitnessKnowledgeCorpusLoader.Validate(duplicate)).Message);

        var missing = MutableCorpusSource.Clone(valid);
        missing.Items[0].SourceUrl = "";
        Assert.Contains("sourceUrl is required", Assert.Throws<InvalidOperationException>(() => FitnessKnowledgeCorpusLoader.Validate(missing)).Message);

        var oversized = MutableCorpusSource.Clone(valid);
        oversized.Items[0].Text = string.Join(" ", Enumerable.Repeat("kelime", 400));
        Assert.Contains("words", Assert.Throws<InvalidOperationException>(() => FitnessKnowledgeCorpusLoader.Validate(oversized)).Message);
    }

    // ───────────────────── 2-4. Ingestion ─────────────────────

    [Fact]
    public async Task Ingestion_CreatesCollection_AndUpsertsEveryChunkWithMetadata()
    {
        using var h = RagHarness.Create();
        Assert.False(await h.Store.CollectionExistsAsync(h.CollectionName));

        var result = await h.Ingestion.IngestAsync();

        Assert.True(await h.Store.CollectionExistsAsync(h.CollectionName));
        Assert.Equal(h.Corpus.Corpus.Items.Count, result.TotalItems);
        Assert.Equal(result.TotalItems, result.Embedded);
        Assert.Equal(0, result.Unchanged);

        var records = await h.AllRecordsAsync();
        Assert.Equal(h.Corpus.Corpus.Items.Count, records.Count);
        var deload = records.Single(r => r.ChunkId == "deload-definition");
        var source = h.Corpus.Corpus.Items.Single(i => i.Id == "deload-definition");
        Assert.Equal((source.Title, source.Text, source.Category, source.SourceName, source.SourceUrl, source.SourceVersion, source.Language, source.Authority),
                     (deload.Title, deload.Text, deload.Category, deload.SourceName, deload.SourceUrl, deload.SourceVersion, deload.Language, deload.Authority));
        Assert.Equal(FitnessKnowledgeCorpusLoader.ToRecordKey(h.Corpus.Corpus.Corpus, "deload-definition"), deload.Id); // kararlı id
    }

    [Fact]
    public async Task Ingestion_SameCorpusVersion_DoesNotReEmbed()
    {
        using var h = RagHarness.Create();

        var first = await h.Ingestion.IngestAsync();
        var embeddedAfterFirst = h.Embeddings.EmbeddedDocuments;
        var second = await h.Ingestion.IngestAsync();
        var third = await h.Ingestion.IngestAsync();

        Assert.Equal(first.TotalItems, embeddedAfterFirst);
        Assert.Equal(0, second.Embedded);
        Assert.Equal(second.TotalItems, second.Unchanged);
        Assert.Equal(0, third.Embedded);
        Assert.Equal(embeddedAfterFirst, h.Embeddings.EmbeddedDocuments); // ikinci/üçüncü çalıştırmada SIFIR embedding çağrısı
        Assert.Equal(1, h.Embeddings.DocumentCalls);
        Assert.Equal(first.CorpusHash, second.CorpusHash);
    }

    [Fact]
    public async Task Ingestion_ChangedOrRemovedChunks_OnlyChangedIsReEmbedded_RemovedIsDeleted()
    {
        using var h = RagHarness.Create();
        await h.Ingestion.IngestAsync();

        var changed = MutableCorpusSource.Clone(h.Corpus.Corpus);
        changed.Items.Single(i => i.Id == "warm-up-before-resistance-training").SourceVersion = "Motricidade 2021;17 (rev)";
        changed.Items.RemoveAll(i => i.Id == "power-training");
        h.Corpus.Corpus = changed;

        var result = await h.Ingestion.IngestAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Equal(1, result.Deleted);
        var records = await h.AllRecordsAsync();
        Assert.DoesNotContain(records, r => r.ChunkId == "power-training");
        Assert.Equal("Motricidade 2021;17 (rev)", records.Single(r => r.ChunkId == "warm-up-before-resistance-training").SourceVersion);
    }

    [Fact]
    public async Task Ingestion_EmbeddingModelChange_ReEmbedsEverything()
    {
        using var h = RagHarness.Create();
        await h.Ingestion.IngestAsync();

        var otherModel = new HashingEmbeddingService { ModelId = "another-model" };
        var ingestion = new KnowledgeIngestionService(h.Corpus, otherModel,
            new FitnessKnowledgeCollectionProvider(h.Store, otherModel, h.Options), NullLogger<KnowledgeIngestionService>.Instance);

        var result = await ingestion.IngestAsync();

        Assert.Equal(result.TotalItems, result.Embedded);
    }

    // ───────────────────── 5-7. Retrieval ─────────────────────

    [Theory]
    [InlineData("Deload nedir?", "deload_fatigue")]
    [InlineData("Setler arası dinlenme süresi ne olmalı?", "rest_periods")]
    [InlineData("Antrenmana uzun aradan sonra geri dönüş", "return_to_training")]
    public async Task Search_RetrievesRelevantChunk(string query, string expectedCategory)
    {
        using var h = RagHarness.Create(minRelevance: 0.2);
        await h.Ingestion.IngestAsync();

        var result = await h.Search.SearchAsync(query);

        Assert.Equal(KnowledgeSearchStatus.Ok, result.Status);
        Assert.Equal(expectedCategory, result.Hits[0].Category);
        Assert.True(result.Hits.SequenceEqual(result.Hits.OrderByDescending(x => x.Score)));
    }

    [Theory]
    [InlineData(0.30)]
    [InlineData(0.35)]
    [InlineData(0.40)]
    public async Task Search_CandidateThresholdsFilterByConfiguredScore(double threshold)
    {
        using var unfiltered = RagHarness.Create(minRelevance: -1);
        await unfiltered.Ingestion.IngestAsync();
        var raw = await unfiltered.Search.SearchAsync("Bench Press'te plato yaşıyorum, ne yapabilirim?");
        Assert.NotEmpty(raw.Hits);

        using var filtered = RagHarness.Create(minRelevance: threshold);
        await filtered.Ingestion.IngestAsync();
        var result = await filtered.Search.SearchAsync("Bench Press'te plato yaşıyorum, ne yapabilirim?");

        var expected = raw.Hits.Count(hit => hit.Score >= threshold);
        Assert.Equal(expected == 0 ? KnowledgeSearchStatus.NoResults : KnowledgeSearchStatus.Ok, result.Status);
        Assert.Equal(expected, result.Hits.Count);
        Assert.All(result.Hits, hit => Assert.True(hit.Score >= threshold));
    }

    [Fact]
    public async Task Search_UnrelatedQuery_ReturnsNothingAboveThreshold()
    {
        using var h = RagHarness.Create(minRelevance: 0.2);
        await h.Ingestion.IngestAsync();

        var result = await h.Search.SearchAsync("Bitcoin borsa yatırım fiyatları bugün");

        Assert.Equal(KnowledgeSearchStatus.NoResults, result.Status);
        Assert.Empty(result.Hits);

        // Kontrol: eşik olmasa vektör araması yine bir şey döndürürdü; sonuçları eleyen MinRelevanceScore'dur.
        using var noThreshold = RagHarness.Create(minRelevance: -1);
        await noThreshold.Ingestion.IngestAsync();
        var unfiltered = await noThreshold.Search.SearchAsync("Bitcoin borsa yatırım fiyatları bugün");
        Assert.All(unfiltered.Hits, hit => Assert.True(hit.Score < 0.2, $"{hit.ChunkId} scored {hit.Score}"));
    }

    [Fact]
    public async Task Search_MetadataSurvivesRetrieval_AndTopKIsCappedAtThree()
    {
        using var h = RagHarness.Create(minRelevance: -1, topK: 10);
        await h.Ingestion.IngestAsync();

        var result = await h.Search.SearchAsync("deload yorgunluk toparlanma");

        Assert.Equal(RagOptions.MaxTopK, result.Hits.Count); // TopK=10 yapılandırılsa da en fazla 3
        foreach (var hit in result.Hits)
        {
            var item = h.Corpus.Corpus.Items.Single(i => i.Id == hit.ChunkId);
            Assert.Equal(item.Title, hit.Title);
            Assert.Equal(item.Text, hit.Text);
            Assert.Equal(item.SourceName, hit.SourceName);
            Assert.Equal(item.SourceUrl, hit.SourceUrl);
            Assert.Equal(item.SourceVersion, hit.SourceVersion);
            Assert.Equal(item.Language, hit.Language);
            Assert.Equal(item.Authority, hit.Authority);
        }
    }

    // ───────────────────── 8. Tool contract ─────────────────────

    [Fact]
    public void Tool_SearchFitnessKnowledge_TakesOnlyAQuery_NoUserIdOrIdentifiers()
    {
        using var h = RagHarness.Create();
        using var scope = h.Provider.CreateScope();
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        Assert.True(kernel.Plugins.TryGetPlugin(KnowledgePlugin.PluginName, out var plugin));
        var function = Assert.Single(plugin!);
        Assert.Equal("SearchFitnessKnowledge", function.Name);
        var parameter = Assert.Single(function.Metadata.Parameters, p => p.ParameterType != typeof(CancellationToken));
        Assert.Equal("query", parameter.Name);
        Assert.True(parameter.IsRequired);
        Assert.Contains("NO data about the user", function.Description);

        // Plugin yalnızca bilgi araması ve request-scoped grounding state'ine bağımlıdır: kullanıcı/DB/kimlik bağımlılığı yok.
        var ctorParameters = typeof(KnowledgePlugin).GetConstructors().Single().GetParameters();
        Assert.Equal(new[] { typeof(IFitnessKnowledgeSearchService), typeof(IRagGroundingContext) },
            ctorParameters.Select(p => p.ParameterType).ToArray());
    }

    [Fact]
    public async Task Tool_ModelSuppliedUserIdArgument_IsIgnored_ResultIsCompactAndSourced()
    {
        using var h = RagHarness.Create(minRelevance: 0.2);
        await h.Ingestion.IngestAsync();
        using var scope = h.Provider.CreateScope();
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        var json = (await kernel.InvokeAsync(KnowledgePlugin.PluginName, "SearchFitnessKnowledge",
            new KernelArguments { ["query"] = "Deload nedir?", ["userId"] = "user-B" })).GetValue<string>()!;

        Assert.Equal(new[] { "Deload nedir?" }, h.Embeddings.Queries.ToArray()); // embed edilen yalnızca sorgu
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        var results = doc.RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.InRange(results.Count, 1, 3);
        Assert.Equal(new[] { "score", "sourceName", "sourceUrl", "sourceVersion", "text", "title" },
            results[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Contains("Deload", json); // Türkçe karakterler kaçırılmadan (token tasarrufu)
        Assert.DoesNotContain("\\u0", json);
        Assert.DoesNotContain("user-B", json);
    }

    // ───────────────────── 10. Failure handling ─────────────────────

    [Fact]
    public async Task Tool_BackendFailure_FailsSafe_WithoutLeakingInternals_AndCoolsDown()
    {
        using var h = RagHarness.Create(cooldownSeconds: 60);
        await h.Ingestion.IngestAsync();
        h.Embeddings.FailWith = new HttpRequestException("Connection refused (localhost:11434) qdrant grpc localhost:6334 SECRET-INTERNAL");
        using var scope = h.Provider.CreateScope();
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        var json = (await kernel.InvokeAsync(KnowledgePlugin.PluginName, "SearchFitnessKnowledge",
            new KernelArguments { ["query"] = "deload" })).GetValue<string>()!;

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("unavailable", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("Fitness knowledge search is temporarily unavailable.", doc.RootElement.GetProperty("error").GetString());
        Assert.Empty(doc.RootElement.GetProperty("results").EnumerateArray());
        foreach (var secret in new[] { "11434", "6334", "qdrant", "Qdrant", "SECRET", "HttpRequestException", "localhost" })
            Assert.DoesNotContain(secret, json);

        // Devre kesici: soğuma süresinde backend tekrar denenmez (her chat isteği zaman aşımı beklemez).
        var queriesBefore = h.Embeddings.Queries.Count;
        Assert.Equal(KnowledgeSearchStatus.Unavailable, (await h.Search.SearchAsync("deload")).Status);
        Assert.Equal(queriesBefore, h.Embeddings.Queries.Count);
    }

    [Fact]
    public async Task Search_CollectionNotIngestedYet_IsUnavailable_NotAnException()
    {
        using var h = RagHarness.Create();
        // In-memory store'da koleksiyon hiç oluşturulmadı.
        var result = await h.Search.SearchAsync("deload");
        Assert.True(result.Status is KnowledgeSearchStatus.Unavailable or KnowledgeSearchStatus.NoResults);
    }

    [Fact]
    public async Task Search_EmptyQuery_IsRejected_WithoutCallingTheBackend()
    {
        using var h = RagHarness.Create();
        Assert.Equal(KnowledgeSearchStatus.InvalidQuery, (await h.Search.SearchAsync("   ")).Status);
        Assert.Empty(h.Embeddings.Queries);
        Assert.Equal(FitnessKnowledgeSearchService.MaxQueryLength, FitnessKnowledgeSearchService.Normalize(new string('a', 5000)).Length);
    }

    [Fact]
    public async Task ProductionRegistration_UnreachableBackends_DoNotBreakStartupOrDi_SearchIsUnavailable()
    {
        // Production kaydı (sahte yok): Qdrant/Ollama'ya ulaşılamayan portlar.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Rag:Enabled"] = "true",
            ["Rag:QdrantEndpoint"] = "http://127.0.0.1:9",
            ["Rag:QdrantGrpcPort"] = "9",
            ["Rag:EmbeddingEndpoint"] = "http://127.0.0.1:9",
            ["Rag:TimeoutSeconds"] = "3",
            ["Rag:IngestOnStartup"] = "true"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddFitBotKnowledgePlugin(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // DI çözümlemesi ağa çıkmaz; arka plan ingestion hatası host'u düşürmez.
        var hosted = provider.GetServices<IHostedService>().OfType<KnowledgeIngestionHostedService>().Single();
        await hosted.StartAsync(CancellationToken.None);
        await hosted.ExecuteTask!; // exception fırlatmaz (yalnızca uyarı loglar)
        await hosted.StopAsync(CancellationToken.None);

        var search = provider.GetRequiredService<IFitnessKnowledgeSearchService>();
        Assert.Equal(KnowledgeSearchStatus.Unavailable, (await search.SearchAsync("deload nedir")).Status);
        Assert.Equal(768, provider.GetRequiredService<IFitnessEmbeddingService>().Dimensions); // merkezi katalogdan
    }

    [Fact]
    public void Disabled_RegistersNothing_SoToolsAndPromptStayNonRag()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rag:Enabled"] = "false" }).Build();
        var services = new ServiceCollection();
        services.AddFitBotKnowledgePlugin(configuration);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(KnowledgePlugin));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(KernelPlugin));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
    }

    // ───────────────────── Embedding provider (Ollama) ─────────────────────

    [Fact]
    public async Task Ollama_SendsModelAndTaskPrefixes_ToApiEmbed_AndValidatesDimensions()
    {
        var handler = new EmbedStubHandler(dimensions: 768);
        var service = CreateOllama(handler, "nomic-embed-text-v2-moe");

        var query = await service.EmbedQueryAsync("deload nedir");
        var documents = await service.EmbedDocumentsAsync(new[] { "a", "b" });

        Assert.Equal(768, query.Length);
        Assert.Equal(2, documents.Count);
        Assert.All(handler.Requests, r => Assert.Equal("/api/embed", r.Path));
        using (var q = JsonDocument.Parse(handler.Requests[0].Body))
        {
            Assert.Equal("nomic-embed-text-v2-moe", q.RootElement.GetProperty("model").GetString());
            Assert.Equal("search_query: deload nedir", q.RootElement.GetProperty("input")[0].GetString());
            Assert.True(q.RootElement.GetProperty("truncate").GetBoolean());
        }
        using (var d = JsonDocument.Parse(handler.Requests[1].Body))
            Assert.Equal("search_document: a", d.RootElement.GetProperty("input")[0].GetString());
        Assert.DoesNotContain(handler.Requests, r => r.HasAuthorization); // API key yok

        var mismatch = CreateOllama(new EmbedStubHandler(dimensions: 384), "nomic-embed-text-v2-moe");
        await Assert.ThrowsAsync<InvalidOperationException>(() => mismatch.EmbedQueryAsync("x"));
    }

    [Fact]
    public void EmbeddingDimensions_ComeFromCentralCatalog_OrConfiguration()
    {
        Assert.Equal(768, EmbeddingModelCatalog.Resolve("nomic-embed-text-v2-moe", null).Dimensions);
        Assert.Equal(768, EmbeddingModelCatalog.Resolve("nomic-embed-text-v2-moe:latest", null).Dimensions);
        Assert.Equal(256, EmbeddingModelCatalog.Resolve("nomic-embed-text-v2-moe", 256).Dimensions);
        Assert.Equal(0, EmbeddingModelCatalog.Resolve("unknown-model", null).Dimensions);
        Assert.Equal(512, EmbeddingModelCatalog.Resolve("unknown-model", 512).Dimensions);

        var a = CreateOllama(new EmbedStubHandler(768), "nomic-embed-text-v2-moe");
        var b = CreateOllama(new EmbedStubHandler(768), "nomic-embed-text");
        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    private static OllamaFitnessEmbeddingService CreateOllama(EmbedStubHandler handler, string model)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(OllamaFitnessEmbeddingService.HttpClientName, c => c.BaseAddress = new Uri("http://localhost:11434/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return new OllamaFitnessEmbeddingService(factory, Options.Create(new RagOptions { EmbeddingModel = model }));
    }

    private sealed record EmbedRequestCapture(string Path, string Body, bool HasAuthorization);

    private sealed class EmbedStubHandler(int dimensions) : HttpMessageHandler
    {
        public List<EmbedRequestCapture> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add(new EmbedRequestCapture(request.RequestUri!.AbsolutePath, body, request.Headers.Authorization is not null));
            using var doc = JsonDocument.Parse(body);
            var count = doc.RootElement.GetProperty("input").GetArrayLength();
            var payload = JsonSerializer.Serialize(new { embeddings = Enumerable.Range(0, count).Select(_ => Enumerable.Repeat(0.1f, dimensions)).ToArray() });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "FitTracker.API.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    /// <summary>Production kaydı + in-memory vector store + deterministik embedding.</summary>
    internal sealed class RagHarness : IDisposable
    {
        public ServiceProvider Provider { get; }
        public InMemoryVectorStore Store { get; } = new();
        public HashingEmbeddingService Embeddings { get; } = new();
        public MutableCorpusSource Corpus { get; } = new();
        public string CollectionName { get; } = "test_knowledge_" + Guid.NewGuid().ToString("N");
        public IOptions<RagOptions> Options => Provider.GetRequiredService<IOptions<RagOptions>>();
        public IKnowledgeIngestionService Ingestion => Provider.GetRequiredService<IKnowledgeIngestionService>();
        public IFitnessKnowledgeSearchService Search => Provider.GetRequiredService<IFitnessKnowledgeSearchService>();

        private RagHarness(double minRelevance, int topK, int cooldownSeconds)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rag:Enabled"] = "true",
                ["Rag:CollectionName"] = CollectionName,
                ["Rag:TopK"] = topK.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Rag:MinRelevanceScore"] = minRelevance.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Rag:UnavailableCooldownSeconds"] = cooldownSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IFitnessEmbeddingService>(Embeddings);
            services.AddKeyedSingleton<VectorStore>(RagServiceKeys.VectorStore, Store);
            services.AddSingleton<IFitnessKnowledgeCorpusSource>(Corpus);
            services.AddFitBotKnowledgePlugin(configuration);
            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        public static RagHarness Create(double minRelevance = 0.5, int topK = 3, int cooldownSeconds = 0) => new(minRelevance, topK, cooldownSeconds);

        public async Task<List<FitnessKnowledgeRecord>> AllRecordsAsync()
        {
            var collection = Provider.GetRequiredService<FitnessKnowledgeCollectionProvider>().Get();
            var list = new List<FitnessKnowledgeRecord>();
            await foreach (var record in collection.GetAsync(r => r.ChunkId != "", 1000))
                list.Add(record);
            return list;
        }

        public void Dispose() => Provider.Dispose();
    }
}
