using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace FitTrackr.API.RAG
{
    /// <summary>Version-control edilen bilgi kaynağı (<c>RAG/Knowledge/fitness_knowledge.json</c>).</summary>
    public sealed class FitnessKnowledgeCorpus
    {
        [JsonPropertyName("corpus")] public string Corpus { get; set; } = string.Empty;
        [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("items")] public List<FitnessKnowledgeItem> Items { get; set; } = new();
    }

    /// <summary>Tek bir bilinçli retrieval chunk'ı. Ingestion sırasında ayrıca bölünmez.</summary>
    public sealed class FitnessKnowledgeItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
        [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
        [JsonPropertyName("language")] public string Language { get; set; } = string.Empty;

        /// <summary>"current": güncel/otoriter kaynak. "historical": güncellenmiş eski öneri (yalnızca bağlam için).</summary>
        [JsonPropertyName("authority")] public string Authority { get; set; } = string.Empty;

        [JsonPropertyName("sourceName")] public string SourceName { get; set; } = string.Empty;
        [JsonPropertyName("sourceUrl")] public string SourceUrl { get; set; } = string.Empty;
        [JsonPropertyName("sourceVersion")] public string SourceVersion { get; set; } = string.Empty;
    }

    public interface IFitnessKnowledgeCorpusSource
    {
        FitnessKnowledgeCorpus Load();
    }

    /// <summary>Corpus'u gömülü kaynaktan (varsayılan) veya <see cref="RagOptions.CorpusPath"/> dosyasından yükler.</summary>
    public sealed class FitnessKnowledgeCorpusSource : IFitnessKnowledgeCorpusSource
    {
        private readonly string? _path;

        public FitnessKnowledgeCorpusSource(IOptions<RagOptions> options) => _path = options.Value.CorpusPath;

        public FitnessKnowledgeCorpus Load() =>
            string.IsNullOrWhiteSpace(_path)
                ? FitnessKnowledgeCorpusLoader.LoadEmbedded()
                : FitnessKnowledgeCorpusLoader.Parse(File.ReadAllText(_path));
    }

    public static class FitnessKnowledgeCorpusLoader
    {
        public const string EmbeddedResourceName = "FitTrackr.API.RAG.Knowledge.fitness_knowledge.json";

        public const int MinWords = 50;
        public const int MaxWords = 300;

        private static readonly HashSet<string> Languages = new(StringComparer.Ordinal) { "tr", "en" };
        private static readonly HashSet<string> Authorities = new(StringComparer.Ordinal) { "current", "historical" };

        public static FitnessKnowledgeCorpus LoadEmbedded()
        {
            using var stream = typeof(FitnessKnowledgeCorpusLoader).Assembly.GetManifestResourceStream(EmbeddedResourceName)
                               ?? throw new InvalidOperationException($"Embedded knowledge corpus '{EmbeddedResourceName}' not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Parse(reader.ReadToEnd());
        }

        public static FitnessKnowledgeCorpus Parse(string json)
        {
            var corpus = JsonSerializer.Deserialize<FitnessKnowledgeCorpus>(json)
                         ?? throw new InvalidOperationException("Knowledge corpus is empty.");
            Validate(corpus);
            return corpus;
        }

        /// <summary>Bozuk/eksik metadata'lı veya duplicate chunk'lı bir corpus hiç ingest edilmez.</summary>
        public static void Validate(FitnessKnowledgeCorpus corpus)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(corpus.Corpus)) errors.Add("corpus name is required");
            if (string.IsNullOrWhiteSpace(corpus.Version)) errors.Add("corpus version is required");
            if (corpus.Items.Count == 0) errors.Add("corpus has no items");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var texts = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in corpus.Items)
            {
                var label = string.IsNullOrWhiteSpace(item.Id) ? "<missing id>" : item.Id;
                foreach (var (field, value) in new[]
                         {
                             ("id", item.Id), ("title", item.Title), ("text", item.Text), ("category", item.Category),
                             ("language", item.Language), ("authority", item.Authority), ("sourceName", item.SourceName),
                             ("sourceUrl", item.SourceUrl), ("sourceVersion", item.SourceVersion)
                         })
                {
                    if (string.IsNullOrWhiteSpace(value))
                        errors.Add($"{label}: {field} is required");
                }

                if (!string.IsNullOrWhiteSpace(item.Id) && !ids.Add(item.Id.Trim())) errors.Add($"{label}: duplicate id");
                if (!string.IsNullOrWhiteSpace(item.Title) && !titles.Add(item.Title.Trim())) errors.Add($"{label}: duplicate title");
                if (!string.IsNullOrWhiteSpace(item.Text) && !texts.Add(item.Text.Trim())) errors.Add($"{label}: duplicate text");

                if (!string.IsNullOrWhiteSpace(item.Language) && !Languages.Contains(item.Language)) errors.Add($"{label}: unsupported language '{item.Language}'");
                if (!string.IsNullOrWhiteSpace(item.Authority) && !Authorities.Contains(item.Authority)) errors.Add($"{label}: unsupported authority '{item.Authority}'");
                if (!string.IsNullOrWhiteSpace(item.SourceUrl)
                    && !(Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"))
                    errors.Add($"{label}: sourceUrl must be an absolute http(s) URL");

                var words = CountWords(item.Text);
                if (!string.IsNullOrWhiteSpace(item.Text) && (words < MinWords || words > MaxWords))
                    errors.Add($"{label}: text has {words} words (expected {MinWords}-{MaxWords})");
            }

            if (errors.Count > 0)
                throw new InvalidOperationException("Invalid knowledge corpus: " + string.Join("; ", errors));
        }

        public static int CountWords(string? text) =>
            string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        /// <summary>
        /// Bir item'ın içerik + embedding yapılandırması hash'i. Aynıysa kayıt yeniden embed EDİLMEZ (deterministik,
        /// sıralamadan bağımsız). Metadata değişikliği de hash'i değiştirir; böylece Qdrant'taki metadata hep güncel kalır.
        /// </summary>
        public static string ComputeItemHash(FitnessKnowledgeItem item, string embeddingFingerprint) =>
            Sha256Hex(string.Join("\u001f",
                embeddingFingerprint, item.Id.Trim(), item.Title, item.Text, item.Category, item.Language,
                item.Authority, item.SourceName, item.SourceUrl, item.SourceVersion));

        /// <summary>Corpus'un tamamının hash'i (sürüm/teşhis amaçlı; item hash'lerinin sıralı birleşimi).</summary>
        public static string ComputeCorpusHash(FitnessKnowledgeCorpus corpus, string embeddingFingerprint) =>
            Sha256Hex(string.Join("\n", corpus.Items
                .Select(i => ComputeItemHash(i, embeddingFingerprint))
                .OrderBy(h => h, StringComparer.Ordinal)));

        /// <summary>Kararlı chunk id'sinden kararlı Qdrant point id'si (Guid).</summary>
        public static Guid ToRecordKey(string corpusName, string itemId)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{corpusName}:{itemId.Trim().ToLowerInvariant()}"));
            return new Guid(hash.AsSpan(0, 16));
        }

        private static string Sha256Hex(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
