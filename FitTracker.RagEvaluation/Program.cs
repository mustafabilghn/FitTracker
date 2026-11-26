using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FitTracker.Evaluation.Harness;
using FitTracker.Evaluation.Instrumentation;
using FitTracker.Evaluation.Scenarios;
using FitTrackr.API.RAG;
using Microsoft.Extensions.Configuration;

Console.OutputEncoding = Encoding.UTF8;

const string userSecretsId = "9e85e38b-49f7-43f6-b9fb-595494740b10";
var options = Options.Parse(args);

if (options.Help)
{
    Console.WriteLine(Options.Usage);
    return 0;
}

if (options.List)
{
    foreach (var scenario in ScenarioCatalog.All)
        Console.WriteLine($"{scenario.Id,-28} {scenario.Category,-13} {(ScenarioCatalog.SmokeIds.Contains(scenario.Id) ? "[smoke] " : "")}{scenario.Prompt}");
    return 0;
}
if (options.SelfTest)
    return RagValidation.RunSelfTest();

var repo = RepoInfo.Detect();
var configuration = BuildConfiguration(repo.Root);
var preflight = await PreflightAsync(configuration);
if (!preflight.Ok)
{
    Console.Error.WriteLine($"RAG preflight failed: {preflight.Message}");
    return 2;
}
if (options.Preflight)
{
    Console.WriteLine($"RAG preflight OK: Qdrant collection={preflight.Collection}; Ollama model={preflight.EmbeddingModel}.");
    return 0;
}

var (apiKey, keySource) = ResolveApiKey(userSecretsId);
if (apiKey is null)
{
    Console.Error.WriteLine("Groq API key not found. Set GROQ_API_KEY or configure Groq:ApiKey in FitTracker.API user-secrets.");
    return 2;
}

var scenarios = ScenarioCatalog.Select(options.Suite, options.ScenarioIds);
var outputRoot = options.OutDir ?? Path.Combine(repo.Root, "evaluation", "runs", "rag-current");
var runDir = Path.Combine(outputRoot, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{(options.ScenarioIds.Count > 0 ? "custom" : options.Suite)}-{EvaluationHost.Model.Replace('/', '_')}");
Directory.CreateDirectory(runDir);

var metadata = new
{
    Suite = options.ScenarioIds.Count > 0 ? "custom" : options.Suite,
    ScenarioIds = scenarios.Select(s => s.Id).ToList(),
    ConfiguredModel = EvaluationHost.Model,
    Rag = new
    {
        Enabled = true,
        QdrantEndpoint = preflight.QdrantEndpoint,
        Collection = preflight.Collection,
        EmbeddingEndpoint = preflight.EmbeddingEndpoint,
        EmbeddingModel = preflight.EmbeddingModel,
        Corpus = "existing collection; ingestion disabled"
    },
    RetryPolicy = "no retries",
    GitCommitSha = repo.CommitSha,
    WorkingTreeDirty = repo.WorkingTreeDirty,
    ProductionSourceDirty = repo.ProductionSourceDirty,
    StartedUtc = DateTimeOffset.UtcNow,
    ApiKeySource = keySource,
    MaxLlmRequestsPerCase = options.MaxLlmRequestsPerCase
};

Console.WriteLine($"RAG preflight: OK | collection={preflight.Collection} | embedding model={preflight.EmbeddingModel}");
Console.WriteLine($"Cases: {string.Join(", ", scenarios.Select(s => s.Id))}");
Console.WriteLine($"Output: {runDir}");

var casesPath = Path.Combine(runDir, "cases.jsonl");
var evidencePath = Path.Combine(runDir, "rag-evidence.jsonl");
var json = new JsonSerializerOptions { WriteIndented = false };
var results = new List<(FitTracker.Evaluation.Evaluation.CaseResult Result, bool EffectiveTaskSuccess)>();

for (var i = 0; i < scenarios.Count; i++)
{
    var scenario = scenarios[i];
    Console.Write($"[{i + 1}/{scenarios.Count}] {scenario.Id} ... ");
    var trackedSuccessfulResults =
    new Dictionary<string, string>(StringComparer.Ordinal);

    var trackedObservedKnowledgeResults =
        new Dictionary<string, string>(StringComparer.Ordinal);

    string[] trackedAttemptedTools = [];
    string[] trackedUnavailableTools = [];

    var result = await FitTracker.Evaluation.Evaluation.CaseRunner.RunAsync(
        scenario, apiKey, repo, options.MaxLlmRequestsPerCase,
        (successfulResults, observedKnowledgeResults, attemptedTools, unavailableTools) =>
        {
            foreach (var pair in successfulResults)
                trackedSuccessfulResults[pair.Key] = pair.Value;

            foreach (var pair in observedKnowledgeResults)
                trackedObservedKnowledgeResults[pair.Key] = pair.Value;

            trackedAttemptedTools = attemptedTools.ToArray();
            trackedUnavailableTools = unavailableTools.ToArray();
        });

    var evidence = RagEvidenceRecorder.Drain().ToList();

    if (evidence.Count == 0 &&
     trackedAttemptedTools.Contains(
         RagValidation.KnowledgeTool,
         StringComparer.Ordinal))
    {
        if (trackedObservedKnowledgeResults.TryGetValue(
         RagValidation.KnowledgeTool, out var observedKnowledgeJson))
        {
            evidence.Add(
                RagValidation.CreateEvidenceFromSerializedResult(observedKnowledgeJson));
        }
        else if (trackedSuccessfulResults.TryGetValue(
                     RagValidation.KnowledgeTool, out var knowledgeJson))
        {
            evidence.Add(
                RagValidation.CreateEvidenceFromSerializedResult(knowledgeJson));
        }
        else if (trackedUnavailableTools.Contains(
                     RagValidation.KnowledgeTool, StringComparer.Ordinal))
        {
            evidence.Add(new RagEvidence("unavailable", 0, [], true, null));
        }
        else
        {
            // Araç denendi; sonuç JSON'u gözlemlenemedi.
            evidence.Add(new RagEvidence("no_successful_result", 0, [], false, null));
        }
    }

    var ragValidation = RagValidation.Validate(
        scenario, result, evidence, trackedSuccessfulResults);
    var caseNode = JsonSerializer.SerializeToNode(result, json)!.AsObject();
    caseNode["BaseTaskSuccess"] = result.TaskSuccess;
    caseNode["RagChecks"] = JsonSerializer.SerializeToNode(ragValidation, json);
    caseNode["EffectiveTaskSuccess"] = ragValidation.EffectiveTaskSuccess;
    await File.AppendAllTextAsync(casesPath, caseNode.ToJsonString(json) + Environment.NewLine);
    var knowledgeSelected = result.ToolCallsRequestedByModel.Any(t => t.Name == RagValidation.KnowledgeTool);
    var knowledgeExecuted = evidence.Any(e => e.ToolExecuted);
    var hasRetrieval = evidence.Any(e => e.ToolExecuted && e.Status.Equals("ok", StringComparison.OrdinalIgnoreCase) && e.ResultCount > 0);
    var groundingMeasured = knowledgeExecuted && hasRetrieval;
    await File.AppendAllTextAsync(evidencePath, JsonSerializer.Serialize(new
    {
        ScenarioId = scenario.Id,
        KnowledgeToolSelected = knowledgeSelected,
        KnowledgeToolExecuted = knowledgeExecuted,
        RetrievalResultCount = evidence.Sum(e => e.ResultCount),
        Sources = evidence.SelectMany(e => e.Hits).Select(h => new { h.Title, h.SourceName, h.SourceUrl, h.Category, h.Authority, h.SourceVersion, h.Score }).ToList(),
        Evidence = evidence,
        PersonalAndKnowledgeToolsUsed = ragValidation.PersonalToolExecuted && knowledgeExecuted,
        GroundingSanitizationObserved = groundingMeasured
            ? !result.FinalResponse.Contains("spesifik bir değer", StringComparison.OrdinalIgnoreCase)
            : (bool?)null,
        GroundingSanitizationStatus = groundingMeasured
            ? "retrieval_observed_placeholder_absence_checked"
            : "not_measured_no_successful_retrieval"
    }, json) + Environment.NewLine);
    results.Add((result, ragValidation.EffectiveTaskSuccess));
    Console.WriteLine($"{(ragValidation.EffectiveTaskSuccess ? "PASS" : "FAIL")} | base={result.TaskSuccess} | " +
                      $"llmCalls={result.LlmRequestCount} | knowledge={evidence.Sum(e => e.ResultCount)} | 429={result.RateLimited429}" +
                      (ragValidation.FailureReasons.Count == 0 ? "" : $" | ragFailure={string.Join(",", ragValidation.FailureReasons)}"));
    if (FitTracker.Evaluation.Instrumentation.GroqRateLimitGate.DailyLimitReached)
    {
        var note = $"STOPPED: Groq daily limit reached after {results.Count}/{scenarios.Count} cases. Results are INCOMPLETE. " +
                   FitTracker.Evaluation.Instrumentation.GroqRateLimitGate.DailyLimitDetail;
        Console.WriteLine(note);
        await File.WriteAllTextAsync(Path.Combine(runDir, "INCOMPLETE_DAILY_LIMIT.txt"), note);
        break;
    }

    if (i < scenarios.Count - 1)
        await PaceAsync(result, options.MinRemainingTokens);
}

await File.WriteAllTextAsync(Path.Combine(runDir, "metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
await File.WriteAllTextAsync(Path.Combine(runDir, "summary.json"), JsonSerializer.Serialize(new
{
    metadata,
    Cases = results.Count,
    BaseTaskSuccess = results.Count(r => r.Result.TaskSuccess),
    EffectiveTaskSuccess = results.Count(r => r.EffectiveTaskSuccess),
    TaskSuccess = results.Count(r => r.EffectiveTaskSuccess),
    PlannedCases = scenarios.Count,
    StoppedOnDailyLimit = FitTracker.Evaluation.Instrumentation.GroqRateLimitGate.DailyLimitReached,
    RateLimitRetries = results.Sum(r => r.Result.LlmCalls.Sum(c => c.RateLimitRetries)),
    RateLimitWaitSeconds = results.Sum(r => r.Result.LlmCalls.Sum(c => c.RateLimitWaitMs + c.PreRequestWaitMs)) / 1000.0,
    KnowledgeEvidenceFile = "rag-evidence.jsonl"
}, new JsonSerializerOptions { WriteIndented = true }));

return 0;

static IConfiguration BuildConfiguration(string root) =>
    new ConfigurationBuilder()
        .SetBasePath(root)
        .AddJsonFile(Path.Combine("FitTracker.API", "appsettings.json"), optional: true)
        .AddUserSecrets(userSecretsId)
        .AddEnvironmentVariables()
        .Build();

static (string? Key, string Source) ResolveApiKey(string secretsId)
{
    var key = Environment.GetEnvironmentVariable("GROQ_API_KEY");
    if (!string.IsNullOrWhiteSpace(key))
        return (key, "env:GROQ_API_KEY");
    var secrets = new ConfigurationBuilder().AddUserSecrets(secretsId).Build();
    key = secrets["Groq:ApiKey"];
    return string.IsNullOrWhiteSpace(key) ? (null, "none") : (key, "user-secrets:FitTracker.API");
}

static async Task<(bool Ok, string Message, string QdrantEndpoint, string Collection, string EmbeddingEndpoint, string EmbeddingModel)> PreflightAsync(IConfiguration configuration)
{
    var options = configuration.GetSection(RagOptions.SectionName).Get<RagOptions>();
    if (options is null || !options.Enabled)
        return (false, "Rag:Enabled must be true.", "", "", "", "");

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(2, options.TimeoutSeconds)) };
    try
    {
        var qdrant = new Uri(options.QdrantEndpoint);
        var qdrantResponse = await http.GetAsync($"{options.QdrantEndpoint.TrimEnd('/')}/collections/{Uri.EscapeDataString(options.CollectionName)}");
        if (!qdrantResponse.IsSuccessStatusCode)
            return (false, $"Qdrant collection '{options.CollectionName}' is unavailable (HTTP {(int)qdrantResponse.StatusCode}).", "", "", "", "");

        var ollamaResponse = await http.GetAsync($"{options.EmbeddingEndpoint.TrimEnd('/')}/api/tags");
        if (!ollamaResponse.IsSuccessStatusCode)
            return (false, $"Ollama is unavailable (HTTP {(int)ollamaResponse.StatusCode}).", "", "", "", "");
        var tags = await ollamaResponse.Content.ReadAsStringAsync();
        if (!tags.Contains(options.EmbeddingModel, StringComparison.OrdinalIgnoreCase))
            return (false, $"Ollama embedding model '{options.EmbeddingModel}' is not available.", "", "", "", "");

        return (true, "ok", options.QdrantEndpoint, options.CollectionName, options.EmbeddingEndpoint, options.EmbeddingModel);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
    {
        return (false, $"Qdrant/Ollama connectivity check failed ({ex.GetType().Name}).", "", "", "", "");
    }
}

// Groq free plan (8K TPM): bir sonraki case'ten önce dakikalık token penceresinin yeterince dolmasını bekler.
// RETRY DEĞİLDİR; yalnızca case'ler arasındaki boşluğu ayarlar. Önceki koşuda bu yoktu (sabit 1,5 sn) ve 18/20 case 429 aldı.
static async Task PaceAsync(FitTracker.Evaluation.Evaluation.CaseResult last, int minRemainingTokens)
{
    var lastCall = last.LlmCalls.LastOrDefault();
    var delay = TimeSpan.FromMilliseconds(1500);
    if (last.RateLimited429)
    {
        delay = TimeSpan.FromSeconds(65); // 429 sonrası pencere tamamen sıfırlansın
        Console.WriteLine($"    (pacing: previous case hit 429, waiting {delay.TotalSeconds:F0}s)");
    }
    else if (lastCall?.RateLimitRemainingTokens is { } remaining && remaining < minRemainingTokens)
    {
        var reset = ParseResetDuration(lastCall.RateLimitResetTokens) ?? TimeSpan.FromSeconds(60);
        delay = TimeSpan.FromSeconds(Math.Min(65, reset.TotalSeconds + 1));
        Console.WriteLine($"    (pacing: remaining tokens {remaining}, waiting {delay.TotalSeconds:F1}s)");
    }
    await Task.Delay(delay);
}

static TimeSpan? ParseResetDuration(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;
    var total = 0.0;
    var matched = false;
    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(value, @"(?<n>\d+(?:\.\d+)?)(?<u>ms|h|m|s)"))
    {
        matched = true;
        var n = double.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
        total += m.Groups["u"].Value switch { "ms" => n / 1000, "h" => n * 3600, "m" => n * 60, _ => n };
    }
    return matched ? TimeSpan.FromSeconds(total) : null;
}

sealed class Options
{
    public int MinRemainingTokens { get; private set; } = 6000;
    public bool Help { get; private set; }
    public bool List { get; private set; }
    public bool Preflight { get; private set; }
    public bool SelfTest { get; private set; }
    public string Suite { get; private set; } = "full";
    public string? OutDir { get; private set; }
    public int MaxLlmRequestsPerCase { get; private set; } = 6;
    public List<string> ScenarioIds { get; } = [];

    public static string Usage => "dotnet run --project FitTracker.RagEvaluation -- --list | --preflight | --self-test | --suite full|smoke [--out PATH] [--scenario ID] [--min-remaining-tokens N]";

    public static Options Parse(string[] args)
    {
        var result = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help": result.Help = true; break;
                case "--list": result.List = true; break;
                case "--preflight": result.Preflight = true; break;
                case "--self-test": result.SelfTest = true; break;
                case "--suite" when i + 1 < args.Length: result.Suite = args[++i]; break;
                case "--out" when i + 1 < args.Length: result.OutDir = args[++i]; break;
                case "--scenario" when i + 1 < args.Length: result.ScenarioIds.Add(args[++i]); break;
                case "--min-remaining-tokens" when i + 1 < args.Length: result.MinRemainingTokens = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
            }
        }
        return result;
    }
}

sealed record RagValidationResult(
    bool EffectiveTaskSuccess,
    bool KnowledgeToolSelected,
    bool KnowledgeToolExecuted,
    bool RetrievalAvailable,
    bool PersonalToolExecuted,
    IReadOnlyList<string> FailureReasons);

static class RagValidation
{
    public const string KnowledgeTool = "Knowledge-SearchFitnessKnowledge";

    private static readonly HashSet<string> PersonalReadTools =
    [
        "Workout-GetRecentWorkouts",
        "Workout-GetWeightTrends",
        "Workout-GetPlateauExercises",
        "WorkoutPlan-GetPlannedWorkouts"
    ];

    public static RagValidationResult Validate(
    Scenario scenario,
    FitTracker.Evaluation.Evaluation.CaseResult result,
    IReadOnlyList<RagEvidence> evidence,
    IReadOnlyDictionary<string, string> trackedSuccessfulResults)
    {
        var selected = result.ToolCallsRequestedByModel.Any(t => t.Name == KnowledgeTool);
        var executed = evidence.Any(e => e.ToolExecuted);
        var retrieved = evidence.Any(e =>
            e.ToolExecuted &&
            e.Status.Equals("ok", StringComparison.OrdinalIgnoreCase) &&
            e.ResultCount > 0);
        var personalExecuted =
    result.ToolInvocations.Any(t =>
        PersonalReadTools.Contains(t.Name) && t.Succeeded)
    || trackedSuccessfulResults.Keys.Any(PersonalReadTools.Contains);
        var failures = new List<string>();

        if (scenario.Category is ScenarioCategory.Knowledge or ScenarioCategory.Mixed)
        {
            // Görev başarısı: bilgi araması (model VEYA backend kurtarması ile) gerçekten çalıştı ve passage getirdi mi.
            // Modelin tool'u kendisi seçip seçmediği ayrı bir metriktir (KnowledgeSelected) ve tek başına başarısızlık değildir.
            if (!executed) failures.Add("knowledge_not_executed");
            if (!retrieved) failures.Add("retrieval_empty");
        }

        if (scenario.Category == ScenarioCategory.Mixed && !personalExecuted)
            failures.Add("personal_read_tool_not_executed");

        return new RagValidationResult(
            result.TaskSuccess && failures.Count == 0,
            selected,
            executed,
            retrieved,
            personalExecuted,
            failures);
    }
    public static RagEvidence CreateEvidenceFromSerializedResult(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return new RagEvidence("invalid_root", 0, [], true, null);

            var status =
                root.TryGetProperty("status", out var statusElement) &&
                statusElement.ValueKind == JsonValueKind.String
                    ? statusElement.GetString() ?? "unknown"
                    : "unknown";

            var hits =
                root.TryGetProperty("results", out var results) &&
                results.ValueKind == JsonValueKind.Array
                    ? results.EnumerateArray()
                        .Select(hit => new RagHitEvidence(
                            ReadJsonString(hit, "title"),
                            ReadJsonString(hit, "sourceName"),
                            ReadJsonString(hit, "sourceUrl"),
                            string.Empty,
                            string.Empty,
                            ReadJsonString(hit, "sourceVersion"),
                            ReadJsonDouble(hit, "score")))
                        .ToList()
                    : [];

            return new RagEvidence(status, hits.Count, hits, true, null);
        }
        catch (JsonException ex)
        {
            return new RagEvidence("invalid_json", 0, [], true, ex.GetType().Name);
        }
    }

    private static string ReadJsonString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static double ReadJsonDouble(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.TryGetDouble(out var number)
            ? number
            : 0;
    public static int RunSelfTest()
    {
        var scenario = new Scenario
        {
            Id = "local-mixed",
            Category = ScenarioCategory.Mixed,
            Prompt = "test"
        };
        var result = new FitTracker.Evaluation.Evaluation.CaseResult
        {
            ScenarioId = scenario.Id,
            Category = scenario.Category.ToString(),
            Kind = ScenarioKind.Chat.ToString(),
            ActionType = "free",
            Prompt = scenario.Prompt,
            ConfiguredModel = "test",
            GitCommitSha = "test",
            WorkingTreeDirty = false,
            ProductionSourceDirty = false,
            TimestampUtc = DateTimeOffset.UtcNow,
            TaskSuccess = true,
            FinalResponse = "Güvenli."
        };
        var failed = Validate(
    scenario, result, [],
    new Dictionary<string, string>());
        if (failed.EffectiveTaskSuccess || !failed.FailureReasons.Contains("knowledge_not_executed")
                                       || !failed.FailureReasons.Contains("personal_read_tool_not_executed"))
        {
            Console.Error.WriteLine("RAG self-test failed: missing mixed-tool evidence was accepted.");
            return 1;
        }

        var passed = Validate(
            scenario,
            new FitTracker.Evaluation.Evaluation.CaseResult
            {
                ScenarioId = result.ScenarioId,
                Category = result.Category,
                Kind = result.Kind,
                ActionType = result.ActionType,
                Prompt = result.Prompt,
                ConfiguredModel = result.ConfiguredModel,
                GitCommitSha = result.GitCommitSha,
                WorkingTreeDirty = result.WorkingTreeDirty,
                ProductionSourceDirty = result.ProductionSourceDirty,
                TimestampUtc = result.TimestampUtc,
                TaskSuccess = result.TaskSuccess,
                FinalResponse = result.FinalResponse,
                ToolCallsRequestedByModel = [new ToolCallRequest("k", KnowledgeTool, "{}", [])],
                ToolInvocations =
                [
                    new ToolInvocationRecord
                    {
                        Name = "Workout-GetWeightTrends",
                        Executed = true
                    }
                ]
            },
            [new RagEvidence("ok", 1, [], true, null)],
new Dictionary<string, string>());
        if (!passed.EffectiveTaskSuccess)
        {
            Console.Error.WriteLine("RAG self-test failed: valid mixed-tool evidence was rejected.");
            return 1;
        }
        // 1. Başarılı bilgi araması: kaynak metaverisi doğru okunmalı.
        var okEvidence = CreateEvidenceFromSerializedResult(
            """{"status":"ok","results":[{"title":"Progressive overload","text":"test-only passage","sourceName":"Test Source","sourceUrl":"https://example.org/source","sourceVersion":"v1","score":0.91}]}""");

        if (okEvidence.Status != "ok" ||
            !okEvidence.ToolExecuted ||
            okEvidence.ResultCount != 1 ||
            okEvidence.Hits.Count != 1 ||
            okEvidence.Hits[0].Title != "Progressive overload" ||
            okEvidence.Hits[0].SourceName != "Test Source" ||
            okEvidence.Hits[0].SourceUrl != "https://example.org/source" ||
            okEvidence.Hits[0].SourceVersion != "v1" ||
            Math.Abs(okEvidence.Hits[0].Score - 0.91) > 0.001)
        {
            Console.Error.WriteLine(
                "RAG self-test failed: successful knowledge JSON was not parsed correctly.");
            return 1;
        }

        // 2. Sonuç bulunamaması: arama çalışmış olmalı, retrieval başarılı sayılmamalı.
        var noResultsEvidence = CreateEvidenceFromSerializedResult(
            """{"status":"no_results","results":[]}""");

        if (noResultsEvidence.Status != "no_results" ||
            !noResultsEvidence.ToolExecuted ||
            noResultsEvidence.ResultCount != 0)
        {
            Console.Error.WriteLine(
                "RAG self-test failed: no_results status was not parsed correctly.");
            return 1;
        }

        // 3. Servisin kullanılamaması: bu da araç çağrısından gelen ayrı bir durumdur.
        var unavailableEvidence = CreateEvidenceFromSerializedResult(
            """{"status":"unavailable","error":"test-only error","results":[]}""");

        if (unavailableEvidence.Status != "unavailable" ||
            !unavailableEvidence.ToolExecuted ||
            unavailableEvidence.ResultCount != 0)
        {
            Console.Error.WriteLine(
                "RAG self-test failed: unavailable status was not parsed correctly.");
            return 1;
        }

        Console.WriteLine(
    "RAG-specific self-test passed: mixed-tool validation and knowledge-result parsing are distinguished.");
        return 0;
    }
}
