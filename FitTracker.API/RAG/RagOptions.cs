using System;

namespace FitTrackr.API.RAG
{
    /// <summary>
    /// FitBot genel fitness bilgi tabanı (RAG) yapılandırması. <c>Rag</c> bölümünden okunur.
    /// Lokal geliştirmede (Docker Qdrant + Ollama) secret gerekmez. Production'da <see cref="QdrantApiKey"/> ve
    /// <see cref="EmbeddingApiKey"/> YALNIZCA ortam değişkeni / App Settings / Key Vault referansı ile verilir;
    /// appsettings.json'a yazılmaz ve hiçbir yerde loglanmaz.
    /// </summary>
    public sealed class RagOptions
    {
        public const string SectionName = "Rag";

        public const string OllamaProvider = "Ollama";
        public const string AzureOpenAiProvider = "AzureOpenAI";

        /// <summary>Qdrant API key (Qdrant Cloud / kimlik doğrulamalı Qdrant). Boşsa key gönderilmez (lokal Docker).</summary>
        public string? QdrantApiKey { get; set; }

        /// <summary>Embedding sağlayıcısı: "Ollama" (varsayılan, lokal) veya "AzureOpenAI".</summary>
        public string EmbeddingProvider { get; set; } = OllamaProvider;

        /// <summary>Hosted embedding sağlayıcısının API key'i (AzureOpenAI). Ollama için kullanılmaz.</summary>
        public string? EmbeddingApiKey { get; set; }

        /// <summary>Azure OpenAI deployment adı. Boşsa <see cref="EmbeddingModel"/> kullanılır.</summary>
        public string? EmbeddingDeployment { get; set; }

        /// <summary>Azure OpenAI REST api-version.</summary>
        public string EmbeddingApiVersion { get; set; } = "2024-10-21";

        /// <summary>Tool'un (SearchFitnessKnowledge) sunulduğu tek seferde döndürülebilecek azami sonuç.</summary>
        public const int MaxTopK = 3;

        /// <summary>false ise RAG hiç kaydedilmez: tool listesi ve system prompt NON-RAG ile birebir aynı kalır.</summary>
        public bool Enabled { get; set; }

        /// <summary>Qdrant REST endpoint'i (dashboard/health). .NET client aynı host'a gRPC portundan bağlanır.</summary>
        public string QdrantEndpoint { get; set; } = "http://localhost:6333";

        /// <summary>Qdrant .NET client'ı gRPC kullanır (Docker'da 6334 portu).</summary>
        public int QdrantGrpcPort { get; set; } = 6334;

        public string CollectionName { get; set; } = "fittracker_fitness_knowledge";

        /// <summary>Ollama endpoint'i (lokal embedding; API key gerekmez).</summary>
        public string EmbeddingEndpoint { get; set; } = "http://localhost:11434";

        public string EmbeddingModel { get; set; } = "nomic-embed-text-v2-moe";

        /// <summary>
        /// Boş bırakılırsa boyut <see cref="EmbeddingModelCatalog"/>'tan (merkezi tanım) çözülür.
        /// Katalogda olmayan bir model kullanılıyorsa burada açıkça verilmelidir.
        /// </summary>
        public int? EmbeddingDimensions { get; set; }

        public int TopK { get; set; } = MaxTopK;

        /// <summary>Cosine similarity eşiği: bunun altındaki sonuçlar modele hiç gönderilmez.</summary>
        public double MinRelevanceScore { get; set; } = 0.35;

        /// <summary>Bir passage'ın cevapta KAYNAK olarak gösterilebilmesi için minimum skor (retrieval eşiğinden ayrı).</summary>
        public double CitationMinScore { get; set; } = RagAnswerGrounding.CitationMinScore;

        /// <summary>null: yalnızca Development ortamında açılışta (arka planda) ingestion yapılır.</summary>
        public bool? IngestOnStartup { get; set; }

        /// <summary>Opsiyonel: corpus'u gömülü kaynak yerine bu dosyadan yükle.</summary>
        public string? CorpusPath { get; set; }

        /// <summary>Chat sırasındaki sorgu embedding'i ve Qdrant çağrıları için zaman aşımı. Chat'i uzun süre bekletmemek için kısa tutulur.</summary>
        public int TimeoutSeconds { get; set; } = 15;

        /// <summary>
        /// Ingestion'daki doküman embedding batch'leri için zaman aşımı. CPU'da çalışan lokal modelde batch embedding
        /// sorgudan çok daha uzun sürebilir; arka planda çalıştığı için chat'i bekletmez.
        /// </summary>
        public int IngestionTimeoutSeconds { get; set; } = 300;

        /// <summary>Bir hata sonrası bu süre boyunca arama denenmez (yavaş/erişilemeyen backend chat'i tekrar tekrar bekletmesin).</summary>
        public int UnavailableCooldownSeconds { get; set; } = 30;

        public int EffectiveTopK => Math.Clamp(TopK, 1, MaxTopK);
    }
}
