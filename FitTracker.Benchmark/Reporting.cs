using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FitTracker.Benchmark;

public static class Reporting
{
    public static void WriteJson(BenchmarkReport report, string path)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    public static void WriteMarkdown(BenchmarkReport report, string path)
    {
        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        sb.AppendLine("# FitBot Chat Endpoint — Latency Benchmark");
        sb.AppendLine();
        sb.AppendLine($"- Generated (UTC): {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- Base URL: `{report.BaseUrl}`");
        sb.AppendLine($"- Endpoint: `POST /api/workout/fitbot/chat`");
        sb.AppendLine($"- Action type: `{report.ActionType}`");
        sb.AppendLine($"- Fixed message: `{report.Message}`");
        sb.AppendLine($"- Fresh user per request: {report.FreshUserEveryRequest}");
        sb.AppendLine($"- Warm-up requests (excluded from all stats below): {report.WarmupRequests} ({report.WarmupFailureCount} failed)");
        sb.AppendLine($"- Requests attempted (measured phase only): {report.RequestsAttempted}");
        sb.AppendLine($"- Successful: {report.SuccessCount}  |  Failed: {report.FailureCount}");
        sb.AppendLine($"- Error rate: {report.ErrorRatePercent.ToString("F2", culture)}%");
        sb.AppendLine();

        sb.AppendLine("## Overall latency (successful requests only)");
        sb.AppendLine();
        AppendStatsTable(sb, new (string, StatSummary?)[]
        {
            ("End-to-end API response time (ms)", report.EndToEndMs),
            ("Context retrieval / construction (ms)", report.ContextMs),
            ("Groq API inference (ms)", report.GroqMs),
        });
        sb.AppendLine();

        sb.AppendLine("## Context cache breakdown");
        sb.AppendLine();
        sb.AppendLine($"- Cache hit samples: {report.CacheHitCount}");
        sb.AppendLine($"- Cache miss samples: {report.CacheMissCount}");
        sb.AppendLine($"- Unknown (no cache header returned): {report.CacheUnknownCount}");
        sb.AppendLine();
        AppendStatsTable(sb, new (string, StatSummary?)[]
        {
            ("End-to-end, cache HIT (ms)", report.EndToEndMsCacheHit),
            ("End-to-end, cache MISS (ms)", report.EndToEndMsCacheMiss),
        });
        sb.AppendLine();

        sb.AppendLine("## Measurement notes / what could not be measured");
        sb.AppendLine();
        if (report.Notes.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var note in report.Notes)
                sb.AppendLine($"- {note}");
        }
        sb.AppendLine();

        if (report.FailureMessages.Count > 0)
        {
            sb.AppendLine("## Failed requests");
            sb.AppendLine();
            foreach (var msg in report.FailureMessages)
                sb.AppendLine($"- {msg}");
            sb.AppendLine();
        }

        sb.AppendLine("## Method");
        sb.AppendLine();
        sb.AppendLine("- p95/median use the nearest-rank percentile method on ascending-sorted samples (`ceil(p * n)`-th value).");
        sb.AppendLine("- \"End-to-end API response time\" is measured client-side (wall clock around the full HTTP request/response).");
        sb.AppendLine("- \"Context retrieval / construction\" and \"Groq API inference\" are measured server-side (inside `AiWorkoutCoachService.ChatAsync`) and exposed via the `X-FitBot-Context-Ms` / `X-FitBot-Context-Cache` / `X-FitBot-Groq-Ms` response headers. These headers are measurement-only instrumentation; they do not change the JSON response body, status codes, or any business logic.");
        sb.AppendLine("- If those headers are absent from a response (e.g. benchmarking an older deployed build without this instrumentation), the corresponding sub-metric is left unmeasured for that sample rather than estimated.");

        File.WriteAllText(path, sb.ToString());
    }

    private static void AppendStatsTable(StringBuilder sb, (string Label, StatSummary? Stats)[] rows)
    {
        sb.AppendLine("| Metric | n | mean (ms) | median (ms) | min (ms) | max (ms) | p95 (ms) |");
        sb.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var (label, stats) in rows)
        {
            if (stats is null)
            {
                sb.AppendLine($"| {label} | 0 | N/A | N/A | N/A | N/A | N/A |");
                continue;
            }

            sb.AppendLine(
                $"| {label} | {stats.Count} | {Fmt(stats.Mean)} | {Fmt(stats.Median)} | {Fmt(stats.Min)} | {Fmt(stats.Max)} | {Fmt(stats.P95)} |");
        }
    }

    private static string Fmt(double value) => value.ToString("F1", CultureInfo.InvariantCulture);
}
