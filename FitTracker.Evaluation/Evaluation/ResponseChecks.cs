using System.Globalization;
using System.Text.RegularExpressions;

namespace FitTracker.Evaluation.Evaluation;

/// <summary>
/// Final cevap üzerinde deterministik kontroller. Kalıplar production system prompt'undaki açık kurallardan alınmıştır
/// (AiWorkoutCoachService.BuildSystemPromptTurkish); production sanitization'ından SONRA kalan ihlalleri ölçer.
/// </summary>
public static partial class ResponseChecks
{
    private static readonly CultureInfo Tr = new("tr-TR");

    // ── Yasak kalıplar (production prompt kuralları) ──
    private static readonly (string Id, Regex Pattern)[] ForbiddenPatterns =
    [
        ("formal_address_siz", new Regex(@"(?<![\p{L}])(siz|sizin|sizi|size|sizinle)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("we_form_verb", new Regex(@"\p{L}+(abiliriz|ebiliriz)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("conclusion_filler", new Regex(@"(?i)(sonuç olarak|özetle|bu nedenle)")),
        ("needs_more_data", new Regex(@"(?i)daha fazla (veri|bilgi|veriye|bilgiye)")),
        ("asks_user_question", new Regex(@"(?i)(ister misin|ister misiniz)\s*\?")),
        ("foreign_script", ForeignScript()),
    ];

    [GeneratedRegex(@"[Ѐ-ӿ぀-ヿ㐀-鿿가-힯]")]
    private static partial Regex ForeignScript();

    [GeneratedRegex(@"[çğıöşüÇĞİÖŞÜ]")]
    private static partial Regex TurkishLetters();

    private static readonly HashSet<string> TurkishStopwords =
        ["ve", "bir", "bu", "için", "ile", "da", "de", "çok", "daha", "olarak", "gibi", "sen", "senin", "her", "ama", "veya", "en", "mi", "ne", "olan"];

    private static readonly HashSet<string> EnglishStopwords =
        ["the", "and", "you", "your", "for", "with", "this", "that", "is", "are", "to", "of", "in", "it", "be", "can", "should", "will", "on", "or"];

    // "kaydedildi/kaydettim/saved" — olumsuz biçimler ("kaydedilmedi", "kaydedilemedi", "not saved") eşleşmez.
    [GeneratedRegex(@"(?i)(kaydettim|kaydettik|kaydedildi(?!\p{L})|kaydedilmiştir|kayıt (edildi|tamamlandı)|(?<!not )\bsaved\b)")]
    private static partial Regex SaveClaim();

    [GeneratedRegex(@"(?<kg>\d+(?:[.,]\d+)?)\s*kg", RegexOptions.IgnoreCase)]
    private static partial Regex Kilograms();

    public static IReadOnlyList<string> FindForbiddenPatterns(string text) =>
        ForbiddenPatterns.Where(p => p.Pattern.IsMatch(text)).Select(p => p.Id).ToList();

    /// <summary>Türkçe dil uyumu: yabancı alfabe yok ve Türkçe işaretleri İngilizceden baskın.</summary>
    public static (bool Ok, string Detail) TurkishLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (false, "empty");
        if (ForeignScript().IsMatch(text))
            return (false, "foreign script characters");

        var tokens = Tokenize(text);
        var tr = tokens.Count(t => TurkishStopwords.Contains(t));
        var en = tokens.Count(t => EnglishStopwords.Contains(t));
        var hasTurkishLetters = TurkishLetters().IsMatch(text);
        var ok = en <= tr && (hasTurkishLetters || tr > 0);
        return (ok, $"tr_stopwords={tr} en_stopwords={en} turkish_letters={hasTurkishLetters}");
    }

    public static bool ClaimsSave(string text) => SaveClaim().IsMatch(text);

    /// <summary>Bench Press'in geçtiği cümlelerdeki kg değerleri.</summary>
    public static IReadOnlyList<double> BenchPressWeights(string text)
    {
        var weights = new List<double>();
        foreach (var sentence in Regex.Split(text, @"(?<=[.!?\n])"))
        {
            if (sentence.IndexOf("bench", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            foreach (Match m in Kilograms().Matches(sentence))
                if (double.TryParse(m.Groups["kg"].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var kg))
                    weights.Add(kg);
        }
        return weights;
    }

    public static bool ContainsCi(string text, string fragment) =>
        Tr.CompareInfo.IndexOf(text, fragment, CompareOptions.IgnoreCase) >= 0;

    public static List<string> Tokenize(string text) =>
        Regex.Replace(text.ToLower(Tr), @"[^\p{L}\p{Nd}]+", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
}
