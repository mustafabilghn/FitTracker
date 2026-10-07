using System.Globalization;
using System.Text.RegularExpressions;
using FitTrackr.API.RAG;

namespace FitTracker.API.Tests;

/// <summary>
/// Deterministik, ağsız embedding: Türkçe küçük harfe çevrilmiş kelimeler sabit bir hash ile vektör boyutlarına dağıtılır
/// (bag-of-words, L2 normalize). Gerçek anlamsal kaliteyi değil, RAG hattının (ingestion, eşik, metadata, hata yolu)
/// doğru çalıştığını test eder. Gerçek model kalitesi manuel smoke ile doğrulanır.
/// </summary>
internal sealed class HashingEmbeddingService : IFitnessEmbeddingService
{
    public const int Dims = 1024;
    private static readonly CultureInfo Tr = new("tr-TR");
    private static readonly HashSet<string> Stopwords =
        ["ve", "bir", "bu", "için", "ile", "çok", "daha", "olarak", "gibi", "veya", "ise", "olan", "kadar", "göre", "nedir", "the", "and"];

    private readonly object _gate = new();

    public string ModelId { get; init; } = "test-hashing";
    public int Dimensions => Dims;
    public string Fingerprint => $"test|{ModelId}|{Dims}";

    public int DocumentCalls { get; private set; }
    public int EmbeddedDocuments { get; private set; }
    public List<string> Queries { get; } = new();
    public Exception? FailWith { get; set; }

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (FailWith is not null) throw FailWith;
            DocumentCalls++;
            EmbeddedDocuments += texts.Count;
        }
        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(texts.Select(t => new ReadOnlyMemory<float>(Embed(t))).ToList());
    }

    public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Queries.Add(query);
            if (FailWith is not null) throw FailWith;
        }
        return Task.FromResult(new ReadOnlyMemory<float>(Embed(query)));
    }

    public static float[] Embed(string text)
    {
        var vector = new float[Dims];
        foreach (var token in Regex.Split(text.ToLower(Tr), @"[^\p{L}\p{Nd}]+"))
        {
            if (token.Length < 3 || Stopwords.Contains(token))
                continue;
            vector[(int)(Fnv1a(token) % Dims)] += 1f;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        return vector;
    }

    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash;
    }
}

/// <summary>Testlerde corpus'u bellekte değiştirebilmek için.</summary>
internal sealed class MutableCorpusSource : IFitnessKnowledgeCorpusSource
{
    public FitnessKnowledgeCorpus Corpus { get; set; } = FitnessKnowledgeCorpusLoader.LoadEmbedded();
    public FitnessKnowledgeCorpus Load() => Corpus;

    public static FitnessKnowledgeCorpus Clone(FitnessKnowledgeCorpus corpus) => new()
    {
        Corpus = corpus.Corpus,
        Version = corpus.Version,
        Description = corpus.Description,
        Items = corpus.Items.Select(i => new FitnessKnowledgeItem
        {
            Id = i.Id, Title = i.Title, Text = i.Text, Category = i.Category, Language = i.Language, Authority = i.Authority,
            SourceName = i.SourceName, SourceUrl = i.SourceUrl, SourceVersion = i.SourceVersion
        }).ToList()
    };
}
