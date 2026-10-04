namespace FitTracker.Evaluation.Evaluation;

/// <summary>
/// İKİNCİL, keşifsel lexical-overlap metrikleri. Açık uçlu koçluk metninde düşük örtüşme kötü kalite anlamına gelmez;
/// eşik tanımlanmaz ve başarı kararına girmez. Referanslar yeni ve production kurallarıyla uyumludur; eski Llama
/// referansları (evaluation/reference_plans.py) KULLANILMAZ.
/// </summary>
public static class TextMetrics
{
    /// <summary>ROUGE-L F1 (token LCS).</summary>
    public static double RougeL(string reference, string hypothesis)
    {
        var r = ResponseChecks.Tokenize(reference);
        var h = ResponseChecks.Tokenize(hypothesis);
        if (r.Count == 0 || h.Count == 0)
            return 0;

        var dp = new int[r.Count + 1, h.Count + 1];
        for (var i = 1; i <= r.Count; i++)
            for (var j = 1; j <= h.Count; j++)
                dp[i, j] = r[i - 1] == h[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        var lcs = dp[r.Count, h.Count];
        if (lcs == 0)
            return 0;
        var precision = (double)lcs / h.Count;
        var recall = (double)lcs / r.Count;
        return 2 * precision * recall / (precision + recall);
    }

    /// <summary>Corpus BLEU-4, tek referans, NLTK SmoothingFunction().method1 ile aynı yumuşatma (epsilon = 0.1).</summary>
    public static double CorpusBleu4(IReadOnlyList<(string Reference, string Hypothesis)> pairs)
    {
        if (pairs.Count == 0)
            return 0;

        var matched = new double[4];
        var total = new double[4];
        var referenceLength = 0;
        var hypothesisLength = 0;

        foreach (var (reference, hypothesis) in pairs)
        {
            var r = ResponseChecks.Tokenize(reference);
            var h = ResponseChecks.Tokenize(hypothesis);
            referenceLength += r.Count;
            hypothesisLength += h.Count;

            for (var n = 1; n <= 4; n++)
            {
                var refCounts = NGrams(r, n);
                foreach (var (gram, count) in NGrams(h, n))
                {
                    total[n - 1] += count;
                    matched[n - 1] += Math.Min(count, refCounts.GetValueOrDefault(gram));
                }
            }
        }

        if (hypothesisLength == 0)
            return 0;

        var logSum = 0.0;
        for (var n = 0; n < 4; n++)
        {
            if (total[n] == 0)
                return 0;
            var numerator = matched[n] == 0 ? 0.1 : matched[n];
            logSum += 0.25 * Math.Log(numerator / total[n]);
        }

        var brevityPenalty = hypothesisLength >= referenceLength ? 1.0 : Math.Exp(1 - (double)referenceLength / hypothesisLength);
        return brevityPenalty * Math.Exp(logSum);
    }

    private static Dictionary<string, int> NGrams(List<string> tokens, int n)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i + n <= tokens.Count; i++)
        {
            var gram = string.Join('\u0001', tokens.Skip(i).Take(n));
            counts[gram] = counts.GetValueOrDefault(gram) + 1;
        }
        return counts;
    }
}
