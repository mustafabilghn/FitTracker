using System;
using System.Collections.Generic;

namespace FitTrackr.API.RAG
{
    /// <summary>Bir embedding modelinin vektör boyutu ve görev ön ekleri.</summary>
    public sealed record EmbeddingModelProfile(string Model, int Dimensions, string QueryPrefix, string DocumentPrefix);

    /// <summary>
    /// Embedding boyutlarının TEK merkezi tanımı. Kod içinde başka hiçbir yerde boyut sabiti yoktur; koleksiyon şeması,
    /// doğrulama ve içerik hash'i boyutu buradan (veya <see cref="RagOptions.EmbeddingDimensions"/>'tan) alır.
    /// nomic-embed-text modelleri görev ön eki ister ("search_query: " / "search_document: ").
    /// </summary>
    public static class EmbeddingModelCatalog
    {
        private static readonly Dictionary<string, EmbeddingModelProfile> Known = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nomic-embed-text-v2-moe"] = new("nomic-embed-text-v2-moe", 768, "search_query: ", "search_document: "),
            ["nomic-embed-text"] = new("nomic-embed-text", 768, "search_query: ", "search_document: "),
            ["mxbai-embed-large"] = new("mxbai-embed-large", 1024, "Represent this sentence for searching relevant passages: ", string.Empty),
        };

        /// <summary>
        /// Modeli çözer. Ollama etiketi (":latest") yok sayılır. Bilinmeyen bir model için boyut yapılandırmadan gelmezse
        /// <see cref="EmbeddingModelProfile.Dimensions"/> 0 döner; RAG bu durumda kontrollü olarak "unavailable" olur.
        /// </summary>
        public static EmbeddingModelProfile Resolve(string model, int? configuredDimensions)
        {
            var name = (model ?? string.Empty).Trim();
            var lookupName = name.EndsWith(":latest", StringComparison.OrdinalIgnoreCase) ? name[..^":latest".Length] : name;

            if (Known.TryGetValue(lookupName, out var known))
                return configuredDimensions is > 0 ? known with { Model = name, Dimensions = configuredDimensions.Value } : known with { Model = name };

            return new EmbeddingModelProfile(name, configuredDimensions is > 0 ? configuredDimensions.Value : 0, string.Empty, string.Empty);
        }
    }
}
