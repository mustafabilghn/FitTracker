using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FitTrackr.API.RAG
{
    /// <summary>
    /// Embedding üretimi soyutlaması: provider (bugün lokal Ollama) değiştirilebilir. Doküman ve sorgu ayrı metotlardır
    /// çünkü bazı modeller (nomic) farklı görev ön ekleri ister.
    /// </summary>
    public interface IFitnessEmbeddingService
    {
        string ModelId { get; }

        /// <summary>Vektör boyutu (merkezi tanım: <see cref="EmbeddingModelCatalog"/>). 0 ise yapılandırma eksiktir.</summary>
        int Dimensions { get; }

        /// <summary>
        /// Embedding'i etkileyen her şeyin (provider, model, boyut, doküman ön eki) özeti. Değişirse ingestion tüm
        /// kayıtları yeniden embed eder; değişmezse hiçbir şey yeniden üretilmez.
        /// </summary>
        string Fingerprint { get; }

        Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);

        Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default);
    }
}
