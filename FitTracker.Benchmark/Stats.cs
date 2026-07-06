namespace FitTracker.Benchmark;

public static class Stats
{
    // Nearest-rank percentile method: sort ascending, take value at ceil(p * n).
    // Documented explicitly since different percentile conventions give different results.
    public static StatSummary? Summarize(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0)
            return null;

        return new StatSummary
        {
            Count = sorted.Count,
            Mean = sorted.Average(),
            Median = Percentile(sorted, 0.50),
            Min = sorted[0],
            Max = sorted[^1],
            P95 = Percentile(sorted, 0.95)
        };
    }

    private static double Percentile(List<double> sortedAscending, double p)
    {
        if (sortedAscending.Count == 1)
            return sortedAscending[0];

        var rank = (int)Math.Ceiling(p * sortedAscending.Count);
        rank = Math.Clamp(rank, 1, sortedAscending.Count);
        return sortedAscending[rank - 1];
    }
}
