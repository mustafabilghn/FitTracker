using System.Collections.Concurrent;
using System.Text.Json;
using FitTrackr.API.Plugins;
using FitTrackr.API.RAG;
using Microsoft.SemanticKernel;

namespace FitTracker.Evaluation.Instrumentation;

public sealed record RagHitEvidence(
    string Title,
    string SourceName,
    string SourceUrl,
    string Category,
    string Authority,
    string SourceVersion,
    double Score);

public sealed record RagEvidence(
    string Status,
    int ResultCount,
    IReadOnlyList<RagHitEvidence> Hits,
    bool ToolExecuted,
    string? Error);

/// <summary>
/// RAG evaluation'a özgü kanıt kaydıdır. Secret veya passage gövdesi kaydetmez; yalnızca tool sonucu ve metadata'yı tutar.
/// </summary>
public sealed class RagEvidenceRecorder : IAutoFunctionInvocationFilter
{
    private static readonly ConcurrentQueue<RagEvidence> Pending = new();

    public static IReadOnlyList<RagEvidence> Drain()
    {
        var items = new List<RagEvidence>();
        while (Pending.TryDequeue(out var item))
            items.Add(item);
        return items;
    }

    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        if (!string.Equals(context.Function.PluginName, KnowledgePlugin.PluginName, StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        try
        {
            await next(context);
            var json = context.Result?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                Pending.Enqueue(new RagEvidence("unknown", 0, [], true, null));
                return;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString() ?? "unknown"
                : "unknown";
            var hits = root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
                ? results.EnumerateArray().Select(hit => new RagHitEvidence(
                    GetString(hit, "title"),
                    GetString(hit, "sourceName"),
                    GetString(hit, "sourceUrl"),
                    string.Empty,
                    string.Empty,
                    GetString(hit, "sourceVersion"),
                    GetDouble(hit, "score"))).ToList()
                : [];

            Pending.Enqueue(new RagEvidence(
                status,
                hits.Count,
                hits,
                true,
                null));
        }
        catch (Exception ex)
        {
            Pending.Enqueue(new RagEvidence("exception", 0, [], true, ex.GetType().Name));
            throw;
        }
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static double GetDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetDouble(out var number) ? number : 0;
}
