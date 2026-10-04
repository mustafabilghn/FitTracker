using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FitTracker.Evaluation.Harness;

namespace FitTracker.Evaluation.Evaluation;

public sealed record Rate(int Numerator, int Denominator)
{
    public double? Value => Denominator == 0 ? null : Math.Round((double)Numerator / Denominator, 4);
    public override string ToString() => Denominator == 0 ? "n/a" : $"{Numerator}/{Denominator} ({Value!.Value.ToString("P0", CultureInfo.InvariantCulture)})";
}

public sealed class RunMetadata
{
    public required string Suite { get; init; }
    public required IReadOnlyList<string> ScenarioIds { get; init; }
    public required string ConfiguredModel { get; init; }
    public required string GitCommitSha { get; init; }
    public required bool WorkingTreeDirty { get; init; }
    public required bool ProductionSourceDirty { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset FinishedUtc { get; set; }
    public required string ApiKeySource { get; init; }
    public required int MaxLlmRequestsPerCase { get; init; }
    public required int MinRemainingTokensBeforeNextCase { get; init; }
    public List<string> PacingWaits { get; } = new();
    public string Rag => "none (NON-RAG baseline)";
    public string RetryPolicy => "no retries (production SDK retries disabled; evaluator never retries)";
}

/// <summary>Bir çalıştırmanın birincil ve ikincil metrikleri.</summary>
public sealed class RunSummary
{
    public required RunMetadata Run { get; init; }
    public required int Cases { get; init; }

    // Birincil
    public required Rate TaskSuccess { get; init; }
    public required Rate CorrectToolSelection { get; init; }
    public required Rate ToolExecutionSuccess { get; init; }
    public required Rate SaveWorkoutPlanCorrectness { get; init; }
    public required Rate GuardrailCorrectness { get; init; }

    /// <summary>Güvensiz plan senaryoları: yol fark etmeksizin engellenme oranı ve yol dağılımı (ayrı raporlanır).</summary>
    public required Rate UnsafeWritesBlocked { get; init; }
    public required IReadOnlyDictionary<string, int> UnsafeWriteHandling { get; init; }
    public required Rate FalseSaveClaims { get; init; }
    public required Rate CrossUserLeakage { get; init; }
    public required Rate ModelSentIdentityArguments { get; init; }
    public required Rate SecondCompletionAfterWrite { get; init; }
    public required Rate Errors { get; init; }
    public required Rate RateLimited429 { get; init; }
    public required double AverageLatencyMs { get; init; }
    public required double AverageLlmCalls { get; init; }
    public required double? AveragePromptTokens { get; init; }
    public required double? AverageCompletionTokens { get; init; }
    public required double? AverageTotalTokens { get; init; }
    public required IReadOnlyList<string> ObservedModels { get; init; }

    // İkincil
    public required Rate LanguageCorrect { get; init; }
    public required Rate ForbiddenPatternViolations { get; init; }
    public required Rate JsonValid { get; init; }
    public required double? CorpusBleu4 { get; init; }
    public required double? AverageRougeL { get; init; }
    public required int CasesWithReference { get; init; }

    public static RunSummary Compute(RunMetadata run, IReadOnlyList<CaseResult> cases)
    {
        static Rate Count(IEnumerable<CaseResult> source, Func<CaseResult, bool?> selector)
        {
            var values = source.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return new Rate(values.Count(v => v), values.Count);
        }

        double? Avg(Func<CaseResult, int?> selector)
        {
            var values = cases.Select(selector).Where(v => v.HasValue).Select(v => (double)v!.Value).ToList();
            return values.Count == 0 ? null : Math.Round(values.Average(), 1);
        }

        var withReference = cases.Where(c => c.Reference is not null && c.FinalResponse.Length > 0).ToList();

        return new RunSummary
        {
            Run = run,
            Cases = cases.Count,
            TaskSuccess = Count(cases, c => c.TaskSuccess),
            CorrectToolSelection = Count(cases, c => c.CorrectToolSelection),
            ToolExecutionSuccess = new Rate(cases.Sum(c => c.ToolInvocations.Count(t => t.Succeeded)), cases.Sum(c => c.ToolInvocations.Count)),
            SaveWorkoutPlanCorrectness = Count(cases, c => c.WriteCorrect),
            GuardrailCorrectness = Count(cases, c => c.GuardrailCorrect),
            UnsafeWritesBlocked = Count(cases, c => c.UnsafeWriteBlocked),
            UnsafeWriteHandling = cases.Where(c => c.UnsafeWriteHandling is not null)
                .GroupBy(c => c.UnsafeWriteHandling!).ToDictionary(g => g.Key, g => g.Count()),
            FalseSaveClaims = Count(cases, c => c.FalseSaveClaim),
            CrossUserLeakage = Count(cases, c => c.CrossUserLeak),
            ModelSentIdentityArguments = Count(cases, c => c.ModelSentIdentityLikeArgument),
            SecondCompletionAfterWrite = Count(cases, c => c.SecondCompletionAfterWrite),
            Errors = Count(cases, c => c.Error is not null),
            RateLimited429 = Count(cases, c => c.RateLimited429),
            AverageLatencyMs = cases.Count == 0 ? 0 : Math.Round(cases.Average(c => c.LatencyMs), 0),
            AverageLlmCalls = cases.Count == 0 ? 0 : Math.Round(cases.Average(c => c.LlmRequestCount), 2),
            AveragePromptTokens = Avg(c => c.PromptTokens),
            AverageCompletionTokens = Avg(c => c.CompletionTokens),
            AverageTotalTokens = Avg(c => c.TotalTokens),
            ObservedModels = cases.SelectMany(c => c.ObservedRequestModels.Concat(c.ObservedResponseModels)).Distinct().ToList(),
            LanguageCorrect = Count(cases, c => c.LanguageOk),
            ForbiddenPatternViolations = Count(cases, c => c.ForbiddenPatterns.Count > 0),
            JsonValid = Count(cases, c => c.JsonValid),
            CorpusBleu4 = withReference.Count == 0 ? null : Math.Round(TextMetrics.CorpusBleu4(withReference.Select(c => (c.Reference!, c.FinalResponse)).ToList()), 4),
            AverageRougeL = withReference.Count == 0 ? null : Math.Round(withReference.Average(c => c.RougeL ?? 0), 4),
            CasesWithReference = withReference.Count
        };
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToMarkdown(IReadOnlyList<CaseResult> cases)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# FitBot NON-RAG evaluation — `{Run.Suite}`");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Configured model | `{Run.ConfiguredModel}` |");
        sb.AppendLine($"| Observed model(s) on the wire | {string.Join(", ", ObservedModels.Select(m => $"`{m}`"))} |");
        sb.AppendLine($"| Git commit | `{Run.GitCommitSha}` (working tree dirty: {Run.WorkingTreeDirty}, production source dirty: {Run.ProductionSourceDirty}) |");
        sb.AppendLine($"| Started / finished (UTC) | {Run.StartedUtc:u} / {Run.FinishedUtc:u} |");
        sb.AppendLine($"| RAG | {Run.Rag} |");
        sb.AppendLine($"| Retries | {Run.RetryPolicy} |");
        sb.AppendLine($"| Cases | {Cases} ({string.Join(", ", Run.ScenarioIds)}) |");
        sb.AppendLine();
        sb.AppendLine("## Primary metrics");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Task success rate | {TaskSuccess} |");
        sb.AppendLine($"| Correct tool selection rate | {CorrectToolSelection} |");
        sb.AppendLine($"| Tool execution success rate | {ToolExecutionSuccess} |");
        sb.AppendLine($"| SaveWorkoutPlan success/rejection correctness | {SaveWorkoutPlanCorrectness} |");
        sb.AppendLine($"| Guardrail correctness (server-side path) | {GuardrailCorrectness} |");
        sb.AppendLine($"| Unsafe plans blocked (any path) | {UnsafeWritesBlocked} — " +
                      $"{(UnsafeWriteHandling.Count == 0 ? "n/a" : string.Join(", ", UnsafeWriteHandling.Select(kv => $"{kv.Key}: {kv.Value}")))} |");
        sb.AppendLine($"| False save claim rate | {FalseSaveClaims} |");
        sb.AppendLine($"| Cross-user leakage | {CrossUserLeakage} |");
        sb.AppendLine($"| Model sent identity-like tool argument | {ModelSentIdentityArguments} |");
        sb.AppendLine($"| Second LLM completion after write | {SecondCompletionAfterWrite} |");
        sb.AppendLine($"| Error rate | {Errors} |");
        sb.AppendLine($"| 429 rate | {RateLimited429} |");
        sb.AppendLine($"| Average latency | {AverageLatencyMs.ToString(CultureInfo.InvariantCulture)} ms |");
        sb.AppendLine($"| Average LLM calls per case | {AverageLlmCalls.ToString(CultureInfo.InvariantCulture)} |");
        sb.AppendLine($"| Average tokens per case (prompt / completion / total) | {Fmt(AveragePromptTokens)} / {Fmt(AverageCompletionTokens)} / {Fmt(AverageTotalTokens)} |");
        sb.AppendLine();
        sb.AppendLine("## Secondary metrics (exploratory)");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Response language correct (TR) | {LanguageCorrect} |");
        sb.AppendLine($"| Forbidden-pattern violations (after sanitization) | {ForbiddenPatternViolations} |");
        sb.AppendLine($"| JSON valid (where JSON is expected) | {JsonValid} |");
        sb.AppendLine($"| Corpus BLEU-4 / avg ROUGE-L (cases with new references: {CasesWithReference}) | {Fmt(CorpusBleu4)} / {Fmt(AverageRougeL)} |");
        sb.AppendLine();
        sb.AppendLine("BLEU/ROUGE are exploratory only; no threshold, not part of task success. Legacy Llama references are not used.");
        sb.AppendLine();
        sb.AppendLine("## Cases");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Category | Success | LLM calls | Tools requested by model | Save | DB planned Δ | Latency | Tokens | Failed required checks |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var c in cases)
        {
            var tools = c.ToolCallsRequestedByModel.Count == 0 ? "—" : string.Join(", ", c.ToolCallsRequestedByModel.Select(t => t.Name));
            var save = c.SaveWorkoutPlan is null ? "—" : c.SaveWorkoutPlan.Success ? "success" : c.SaveWorkoutPlan.Reason;
            var delta = c.DbAfter!.PrimaryPlanned - c.DbBefore!.PrimaryPlanned;
            var failed = c.Checks.Where(x => x.Required && !x.Passed).Select(x => x.Name).ToList();
            sb.AppendLine($"| `{c.ScenarioId}` | {c.Category} | {(c.TaskSuccess ? "✅" : "❌")} | {c.LlmRequestCount} | {tools} | {save} | {delta:+0;-0;0} | {c.LatencyMs} ms | {Fmt(c.TotalTokens)} | {(failed.Count == 0 ? "—" : string.Join(", ", failed))} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Final responses");
        foreach (var c in cases)
        {
            sb.AppendLine();
            sb.AppendLine($"### `{c.ScenarioId}`");
            sb.AppendLine();
            sb.AppendLine($"**Prompt:** {c.Prompt}");
            sb.AppendLine();
            sb.AppendLine("**Response:**");
            sb.AppendLine();
            foreach (var line in (c.FinalResponse.Length == 0 ? "(empty)" : c.FinalResponse).Split('\n'))
                sb.AppendLine($"> {line}");
            if (c.Error is not null)
            {
                sb.AppendLine();
                sb.AppendLine($"**Error:** {c.Error}");
            }
        }
        if (Run.PacingWaits.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Rate-limit pacing");
            foreach (var wait in Run.PacingWaits)
                sb.AppendLine($"- {wait}");
        }
        return sb.ToString();
    }

    private static string Fmt(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
    private static string Fmt(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
}
