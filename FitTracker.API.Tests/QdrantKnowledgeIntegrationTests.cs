using CommunityToolkit.VectorData.Qdrant;
using FitTrackr.API.RAG;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// Gerçek lokal Qdrant (Docker, localhost:6333/6334) üzerinde koleksiyon oluşturma, upsert, yeniden embedding yapmama
/// ve metadata'nın retrieval sonrası korunması. Embedding deterministik sahtedir (Ollama gerekmez). Qdrant çalışmıyorsa
/// test SKIP edilir (başarılı sayılmaz). Her test kendi geçici koleksiyonunu oluşturur ve siler; gerçek
/// "fittracker_fitness_knowledge" koleksiyonuna dokunmaz.
/// </summary>
public class QdrantKnowledgeIntegrationTests
{
    private static readonly Lazy<bool> QdrantAvailable = new(() =>
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            return http.GetAsync("http://localhost:6333/readyz").GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    });

    [SkippableFact]
    public async Task Qdrant_CollectionCreation_Upsert_NoReEmbed_AndMetadataSurvivesSearch()
    {
        Skip.IfNot(QdrantAvailable.Value, "Local Qdrant is not running on localhost:6333/6334.");

        var collectionName = "fittracker_test_" + Guid.NewGuid().ToString("N");
        var options = Options.Create(new RagOptions { Enabled = true, CollectionName = collectionName, MinRelevanceScore = 0.2 });
        using var store = new QdrantVectorStore(new QdrantClient("localhost", 6334), ownsClient: true);
        var embeddings = new HashingEmbeddingService();
        var corpus = new MutableCorpusSource();
        var collections = new FitnessKnowledgeCollectionProvider(store, embeddings, options);
        var ingestion = new KnowledgeIngestionService(corpus, embeddings, collections, NullLogger<KnowledgeIngestionService>.Instance);
        var search = new FitnessKnowledgeSearchService(embeddings, collections, new KnowledgeAvailability(), options,
            NullLogger<FitnessKnowledgeSearchService>.Instance);

        try
        {
            Assert.False(await store.CollectionExistsAsync(collectionName));

            var first = await ingestion.IngestAsync();
            Assert.True(await store.CollectionExistsAsync(collectionName));
            Assert.Equal(corpus.Corpus.Items.Count, first.Embedded);

            var second = await ingestion.IngestAsync();
            Assert.Equal(0, second.Embedded);
            Assert.Equal(corpus.Corpus.Items.Count, second.Unchanged);

            var result = await search.SearchAsync("Deload nedir?");
            Assert.Equal(KnowledgeSearchStatus.Ok, result.Status);
            var top = result.Hits[0];
            var item = corpus.Corpus.Items.Single(i => i.Id == top.ChunkId);
            Assert.Equal("deload_fatigue", top.Category);
            Assert.Equal((item.Title, item.Text, item.SourceName, item.SourceUrl, item.SourceVersion, item.Language, item.Authority),
                         (top.Title, top.Text, top.SourceName, top.SourceUrl, top.SourceVersion, top.Language, top.Authority));
        }
        finally
        {
            await store.EnsureCollectionDeletedAsync(collectionName);
        }
    }
}
