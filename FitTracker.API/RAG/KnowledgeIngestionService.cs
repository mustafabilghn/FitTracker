using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace FitTrackr.API.RAG
{
    public sealed record KnowledgeIngestionResult(
        string CollectionName,
        string Corpus,
        string CorpusVersion,
        string CorpusHash,
        int TotalItems,
        int Embedded,
        int Unchanged,
        int Deleted);

    public interface IKnowledgeIngestionService
    {
        /// <summary>
        /// Corpus'u yükler, koleksiyonu (yoksa) oluşturur, yalnızca yeni/değişmiş item'lar için embedding üretip upsert eder
        /// ve corpus'tan çıkarılmış kayıtları siler. Corpus ve embedding yapılandırması değişmediyse hiç embedding üretmez.
        /// </summary>
        Task<KnowledgeIngestionResult> IngestAsync(CancellationToken cancellationToken = default);
    }

    public sealed class KnowledgeIngestionService : IKnowledgeIngestionService
    {
        private const int MaxExistingRecords = 10_000;

        private readonly IFitnessKnowledgeCorpusSource _corpusSource;
        private readonly IFitnessEmbeddingService _embeddings;
        private readonly FitnessKnowledgeCollectionProvider _collectionProvider;
        private readonly ILogger<KnowledgeIngestionService> _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public KnowledgeIngestionService(
            IFitnessKnowledgeCorpusSource corpusSource,
            IFitnessEmbeddingService embeddings,
            FitnessKnowledgeCollectionProvider collectionProvider,
            ILogger<KnowledgeIngestionService> logger)
        {
            _corpusSource = corpusSource;
            _embeddings = embeddings;
            _collectionProvider = collectionProvider;
            _logger = logger;
        }

        public async Task<KnowledgeIngestionResult> IngestAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                return await IngestCoreAsync(cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<KnowledgeIngestionResult> IngestCoreAsync(CancellationToken cancellationToken)
        {
            var corpus = _corpusSource.Load(); // geçersiz corpus burada reddedilir (validation)
            var fingerprint = _embeddings.Fingerprint;
            var corpusHash = FitnessKnowledgeCorpusLoader.ComputeCorpusHash(corpus, fingerprint);

            var collection = _collectionProvider.Get();
            await collection.EnsureCollectionExistsAsync(cancellationToken);

            var corpusName = corpus.Corpus;
            var existing = new Dictionary<Guid, FitnessKnowledgeRecord>();
            await foreach (var record in collection.GetAsync(r => r.Corpus == corpusName, MaxExistingRecords,
                               new FilteredRecordRetrievalOptions<FitnessKnowledgeRecord> { IncludeVectors = false },
                               cancellationToken))
            {
                existing[record.Id] = record;
            }

            var pending = new List<FitnessKnowledgeRecord>();
            var currentKeys = new HashSet<Guid>();
            foreach (var item in corpus.Items)
            {
                var key = FitnessKnowledgeCorpusLoader.ToRecordKey(corpusName, item.Id);
                currentKeys.Add(key);
                var hash = FitnessKnowledgeCorpusLoader.ComputeItemHash(item, fingerprint);

                if (existing.TryGetValue(key, out var stored) && string.Equals(stored.ContentHash, hash, StringComparison.Ordinal))
                    continue; // içerik ve embedding yapılandırması aynı: yeniden embedding YOK

                pending.Add(ToRecord(key, corpus, item, hash));
            }

            if (pending.Count > 0)
            {
                var vectors = await _embeddings.EmbedDocumentsAsync(pending.Select(EmbeddingText).ToList(), cancellationToken);
                for (var i = 0; i < pending.Count; i++)
                    pending[i].Embedding = vectors[i];

                await collection.UpsertAsync(pending, cancellationToken);
            }

            var stale = existing.Keys.Where(k => !currentKeys.Contains(k)).ToList();
            if (stale.Count > 0)
                await collection.DeleteAsync(stale, cancellationToken);

            var result = new KnowledgeIngestionResult(
                _collectionProvider.CollectionName, corpusName, corpus.Version, corpusHash,
                corpus.Items.Count, pending.Count, corpus.Items.Count - pending.Count, stale.Count);

            _logger.LogInformation(
                "FitBot knowledge ingestion: collection={Collection} corpus={Corpus}@{Version} hash={Hash} items={Items} embedded={Embedded} unchanged={Unchanged} deleted={Deleted} model={Model}",
                result.CollectionName, result.Corpus, result.CorpusVersion, result.CorpusHash[..12], result.TotalItems,
                result.Embedded, result.Unchanged, result.Deleted, _embeddings.ModelId);

            return result;
        }

        // Başlık da embed edilir: kısa sorguların ("Deload nedir?") başlıkla eşleşmesi retrieval'ı belirgin iyileştirir.
        private static string EmbeddingText(FitnessKnowledgeRecord record) => $"{record.Title}\n{record.Text}";

        private static FitnessKnowledgeRecord ToRecord(Guid key, FitnessKnowledgeCorpus corpus, FitnessKnowledgeItem item, string hash) => new()
        {
            Id = key,
            ChunkId = item.Id.Trim(),
            Corpus = corpus.Corpus,
            CorpusVersion = corpus.Version,
            Title = item.Title,
            Text = item.Text,
            Category = item.Category,
            Language = item.Language,
            Authority = item.Authority,
            SourceName = item.SourceName,
            SourceUrl = item.SourceUrl,
            SourceVersion = item.SourceVersion,
            ContentHash = hash
        };
    }
}
