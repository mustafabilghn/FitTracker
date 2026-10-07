using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;

namespace FitTrackr.API.RAG
{
    /// <summary>
    /// Qdrant'ta saklanan bilgi kaydı. Kaynak metadata'sı (title/source/url/version/language/authority) payload olarak
    /// vektörle birlikte tutulur ve retrieval sonrası aynen geri gelir. Kişisel kullanıcı verisi İÇERMEZ.
    /// Şema attribute'larla değil <see cref="CreateDefinition"/> ile tanımlanır: vektör boyutu sabit kodlanmaz.
    /// </summary>
    public sealed class FitnessKnowledgeRecord
    {
        public Guid Id { get; set; }
        public string ChunkId { get; set; } = string.Empty;
        public string Corpus { get; set; } = string.Empty;
        public string CorpusVersion { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string Authority { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string SourceVersion { get; set; } = string.Empty;

        /// <summary>İçerik + embedding yapılandırması hash'i; değişmediyse yeniden embedding üretilmez.</summary>
        public string ContentHash { get; set; } = string.Empty;

        public ReadOnlyMemory<float> Embedding { get; set; }

        public static VectorStoreCollectionDefinition CreateDefinition(int dimensions) => new()
        {
            Properties = new List<VectorStoreProperty>
            {
                new VectorStoreKeyProperty(nameof(Id), typeof(Guid)),
                new VectorStoreDataProperty(nameof(ChunkId), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(Corpus), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(CorpusVersion), typeof(string)),
                new VectorStoreDataProperty(nameof(Title), typeof(string)),
                new VectorStoreDataProperty(nameof(Text), typeof(string)),
                new VectorStoreDataProperty(nameof(Category), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(Language), typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty(nameof(Authority), typeof(string)),
                new VectorStoreDataProperty(nameof(SourceName), typeof(string)),
                new VectorStoreDataProperty(nameof(SourceUrl), typeof(string)),
                new VectorStoreDataProperty(nameof(SourceVersion), typeof(string)),
                new VectorStoreDataProperty(nameof(ContentHash), typeof(string)),
                new VectorStoreVectorProperty(nameof(Embedding), typeof(ReadOnlyMemory<float>), dimensions)
                {
                    DistanceFunction = DistanceFunction.CosineSimilarity,
                    IndexKind = IndexKind.Hnsw
                }
            }
        };
    }

    /// <summary>
    /// Bilgi koleksiyonuna tek erişim noktası. Vector store DI'da <see cref="RagServiceKeys.VectorStore"/> anahtarıyla
    /// kayıtlıdır (production: Qdrant, testler: in-memory). Koleksiyon nesnesi ilk kullanımda oluşturulur; oluşturma
    /// ağ çağrısı yapmaz.
    /// </summary>
    public sealed class FitnessKnowledgeCollectionProvider
    {
        private readonly VectorStore _vectorStore;
        private readonly IFitnessEmbeddingService _embeddings;
        private readonly string _collectionName;
        private readonly object _gate = new();
        private VectorStoreCollection<Guid, FitnessKnowledgeRecord>? _collection;

        public FitnessKnowledgeCollectionProvider(
            [FromKeyedServices(RagServiceKeys.VectorStore)] VectorStore vectorStore,
            IFitnessEmbeddingService embeddings,
            IOptions<RagOptions> options)
        {
            _vectorStore = vectorStore;
            _embeddings = embeddings;
            _collectionName = options.Value.CollectionName;
        }

        public string CollectionName => _collectionName;

        public VectorStoreCollection<Guid, FitnessKnowledgeRecord> Get()
        {
            lock (_gate)
            {
                if (_collection is not null)
                    return _collection;

                if (_embeddings.Dimensions <= 0)
                    throw new InvalidOperationException(
                        $"Embedding dimensions are unknown for model '{_embeddings.ModelId}'. Set Rag:EmbeddingDimensions.");

                return _collection = _vectorStore.GetCollection<Guid, FitnessKnowledgeRecord>(
                    _collectionName, FitnessKnowledgeRecord.CreateDefinition(_embeddings.Dimensions));
            }
        }
    }

    public static class RagServiceKeys
    {
        public const string VectorStore = "FitBotKnowledge";
    }
}
