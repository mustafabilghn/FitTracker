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
    var result = await FitTracker.Evaluation.Evaluation.CaseRunner.RunAsync(
        scenario, apiKey, repo, options.MaxLlmRequestsPerCase);
    var evidence = RagEvidenceRecorder.Drain();
    var ragValidation = RagValidation.Validate(scenario, result, evidence);
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
    if (i < scenarios.Count - 1)
        await Task.Delay(1500);
}

await File.WriteAllTextAsync(Path.Combine(runDir, "metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
await File.WriteAllTextAsync(Path.Combine(runDir, "summary.json"), JsonSerializer.Serialize(new
{
    metadata,
    Cases = results.Count,
    BaseTaskSuccess = results.Count(r => r.Result.TaskSuccess),
    EffectiveTaskSuccess = results.Count(r => r.EffectiveTaskSuccess),
    TaskSuccess = results.Count(r => r.EffectiveTaskSuccess),
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

sealed class Options
{
    public bool Help { get; private set; }
    public bool List { get; private set; }
    public bool Preflight { get; private set; }
    public bool SelfTest { get; private set; }
    public string Suite { get; private set; } = "full";
    public string? OutDir { get; private set; }
    public int MaxLlmRequestsPerCase { get; private set; } = 6;
    public List<string> ScenarioIds { get; } = [];

    public static string Usage => "dotnet run --project FitTracker.RagEvaluation -- --list | --preflight | --self-test | --suite full|smoke [--out PATH] [--scenario ID]";

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
        IReadOnlyList<RagEvidence> evidence)
    {
        var selected = result.ToolCallsRequestedByModel.Any(t => t.Name == KnowledgeTool);
        var executed = evidence.Any(e => e.ToolExecuted);
        var retrieved = evidence.Any(e =>
            e.ToolExecuted &&
            e.Status.Equals("ok", StringComparison.OrdinalIgnoreCase) &&
            e.ResultCount > 0);
        var personalExecuted = result.ToolInvocations.Any(t =>
            PersonalReadTools.Contains(t.Name) && t.Succeeded);
        var failures = new List<string>();

        if (scenario.Category is ScenarioCategory.Knowledge or ScenarioCategory.Mixed)
        {
            if (!selected) failures.Add("knowledge_not_selected");
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
        var failed = Validate(scenario, result, []);
        if (failed.EffectiveTaskSuccess || !failed.FailureReasons.Contains("knowledge_not_selected"))
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
            [new RagEvidence("ok", 1, [], true, null)]);
        if (!passed.EffectiveTaskSuccess)
        {
            Console.Error.WriteLine("RAG self-test failed: valid mixed-tool evidence was rejected.");
            return 1;
        }

        Console.WriteLine("RAG-specific self-test passed: missing and valid mixed-tool evidence are distinguished.");
        return 0;
    }
}
