using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FitTracker.Evaluation.Evaluation;
using FitTracker.Evaluation.Harness;
using FitTracker.Evaluation.Scenarios;
using FitTrackr.API.Repositories;
using FitTrackr.API.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// FitBot NON-RAG baseline evaluator — production C# yolu üzerinden (bkz. README.md).
Console.OutputEncoding = Encoding.UTF8;

const string ApiUserSecretsId = "9e85e38b-49f7-43f6-b9fb-595494740b10"; // FitTracker.API/FitTrackr.API.csproj

var options = Options.Parse(args);
if (options.Help)
{
    Console.WriteLine(Options.Usage);
    return 0;
}

if (options.List)
{
    foreach (var s in ScenarioCatalog.All)
        Console.WriteLine($"{s.Id,-28} {s.Category,-13} {(ScenarioCatalog.SmokeIds.Contains(s.Id) ? "[smoke] " : "")}{s.Prompt}");
    return 0;
}

var repo = RepoInfo.Detect();

if (options.FixturesOnly)
{
    // Token harcamaz: fixture'ı production yoluyla kurar ve Completed/Planned ayrımını gösterir.
    await using var host = EvaluationHost.Create("fixtures-only-no-llm-calls", maxLlmRequestsPerCase: 0);
    await FixtureBuilder.SeedAsync(host.Services, DateTime.UtcNow.Date);
    await using var scope = host.Services.CreateAsyncScope();
    var context = await scope.ServiceProvider.GetRequiredService<IWorkoutAnalysisService>().GetFitBotContextAsync(EvalUsers.Primary);
    var planned = await scope.ServiceProvider.GetRequiredService<IWorkoutRepository>().GetPlannedAsync(EvalUsers.Primary);
    Console.WriteLine($"Completed (analysis) total={context.TotalWorkouts} last30={context.WorkoutsLast30Days} daysSinceLast={context.DaysSinceLastWorkout}");
    foreach (var t in context.WeightTrends)
        Console.WriteLine($"  trend {t.ExerciseName}: {string.Join(" ", t.WeeklyMaxWeights.OrderByDescending(w => w.WeeksAgo).Select(w => $"w{w.WeeksAgo}={w.MaxKg}"))} {t.Trend}");
    Console.WriteLine($"  plateau: [{string.Join(", ", context.PlateauExercises)}]");
    foreach (var p in planned)
        Console.WriteLine($"  planned {p.WorkoutName} {p.WorkoutDate:yyyy-MM-dd} status={p.Status}: {string.Join(", ", p.Exercises.Select(e => $"{e.ExerciseName} {e.ExerciseSets.Max(s => s.WeightInKg)}kg"))}");
    Console.WriteLine($"LLM calls made: {host.LlmCalls.Calls.Count}");
    return 0;
}

var (apiKey, keySource) = ResolveApiKey(ApiUserSecretsId);
if (apiKey is null)
{
    Console.Error.WriteLine("Groq API key not found. Set GROQ_API_KEY or configure Groq:ApiKey in FitTracker.API user-secrets.");
    return 2;
}

var scenarios = ScenarioCatalog.Select(options.Suite, options.ScenarioIds);
var outRoot = options.OutDir ?? Path.Combine(repo.Root, "evaluation", "runs");
var label = options.ScenarioIds.Count > 0 ? "custom" : options.Suite;
var runDir = Path.Combine(outRoot, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{label}-{EvaluationHost.Model.Replace('/', '_')}");
Directory.CreateDirectory(runDir);

var run = new RunMetadata
{
    Suite = label,
    ScenarioIds = scenarios.Select(s => s.Id).ToList(),
    ConfiguredModel = EvaluationHost.Model,
    GitCommitSha = repo.CommitSha,
    WorkingTreeDirty = repo.WorkingTreeDirty,
    ProductionSourceDirty = repo.ProductionSourceDirty,
    StartedUtc = DateTimeOffset.UtcNow,
    ApiKeySource = keySource,
    MaxLlmRequestsPerCase = options.MaxLlmRequestsPerCase,
    MinRemainingTokensBeforeNextCase = options.MinRemainingTokens
};

Console.WriteLine($"Model: {EvaluationHost.Model} | commit {repo.CommitSha[..7]} (dirty={repo.WorkingTreeDirty}, production dirty={repo.ProductionSourceDirty}) | key source: {keySource}");
Console.WriteLine($"Cases: {string.Join(", ", run.ScenarioIds)}");
Console.WriteLine($"Output: {runDir}");

var results = new List<CaseResult>();
var casesPath = Path.Combine(runDir, "cases.jsonl");
var lineOptions = new JsonSerializerOptions(RunSummary.JsonOptions) { WriteIndented = false };

for (var i = 0; i < scenarios.Count; i++)
{
    var scenario = scenarios[i];
    Console.Write($"[{i + 1}/{scenarios.Count}] {scenario.Id} ... ");
    var result = await CaseRunner.RunAsync(scenario, apiKey, repo, options.MaxLlmRequestsPerCase);
    results.Add(result);
    await File.AppendAllTextAsync(casesPath, JsonSerializer.Serialize(result, lineOptions) + Environment.NewLine);

    var tools = result.ToolCallsRequestedByModel.Count == 0 ? "-" : string.Join(",", result.ToolCallsRequestedByModel.Select(t => t.Name));
    Console.WriteLine($"{(result.TaskSuccess ? "PASS" : "FAIL")} | llmCalls={result.LlmRequestCount} tools={tools} " +
                      $"save={result.SaveWorkoutPlan?.Reason ?? (result.SaveWorkoutPlan?.Success == true ? "success" : "-")} " +
                      $"latency={result.LatencyMs}ms tokens={result.TotalTokens?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}" +
                      (result.Error is null ? "" : $" error={result.Error}"));

    if (i < scenarios.Count - 1)
        await PaceAsync(result, options.MinRemainingTokens, run.PacingWaits);
}

run.FinishedUtc = DateTimeOffset.UtcNow;
var summary = RunSummary.Compute(run, results);
await File.WriteAllTextAsync(Path.Combine(runDir, "summary.json"), JsonSerializer.Serialize(summary, RunSummary.JsonOptions));
await File.WriteAllTextAsync(Path.Combine(runDir, "report.md"), summary.ToMarkdown(results));

Console.WriteLine();
Console.WriteLine($"Task success {summary.TaskSuccess} | tool selection {summary.CorrectToolSelection} | save correctness {summary.SaveWorkoutPlanCorrectness} | " +
                  $"guardrail {summary.GuardrailCorrectness} | false save claims {summary.FalseSaveClaims} | errors {summary.Errors} | 429 {summary.RateLimited429}");
Console.WriteLine($"Wrote cases.jsonl, summary.json, report.md to {runDir}");
return 0;

// ───────────────────────── helpers ─────────────────────────

static (string? Key, string Source) ResolveApiKey(string userSecretsId)
{
    var fromEnv = Environment.GetEnvironmentVariable("GROQ_API_KEY");
    if (!string.IsNullOrWhiteSpace(fromEnv))
        return (fromEnv, "env:GROQ_API_KEY");

    var secrets = new ConfigurationBuilder().AddUserSecrets(userSecretsId).Build();
    var fromSecrets = secrets["Groq:ApiKey"];
    return string.IsNullOrWhiteSpace(fromSecrets) ? (null, "none") : (fromSecrets, "user-secrets:FitTracker.API");
}

// Groq free plan (8K TPM): kalan dakikalık token azsa bir sonraki case'ten önce pencerenin sıfırlanmasını bekle.
// Bu bir retry DEĞİLDİR; yalnızca case'ler arasındaki boşluğu ayarlar.
static async Task PaceAsync(CaseResult last, int minRemainingTokens, List<string> waits)
{
    var lastCall = last.LlmCalls.LastOrDefault();
    var delay = TimeSpan.FromMilliseconds(1500);
    if (lastCall?.RateLimitRemainingTokens is { } remaining && remaining < minRemainingTokens)
    {
        var reset = ParseDuration(lastCall.RateLimitResetTokens) ?? TimeSpan.FromSeconds(60);
        delay = TimeSpan.FromSeconds(Math.Min(65, reset.TotalSeconds + 1));
        waits.Add($"after {last.ScenarioId}: remaining tokens {remaining} < {minRemainingTokens}, waited {delay.TotalSeconds:F1}s");
        Console.WriteLine($"    (pacing: remaining tokens {remaining}, waiting {delay.TotalSeconds:F1}s)");
    }
    await Task.Delay(delay);
}

static TimeSpan? ParseDuration(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;
    var total = 0.0;
    var matched = false;
    foreach (Match m in Regex.Matches(value, @"(?<n>\d+(?:\.\d+)?)(?<u>ms|h|m|s)"))
    {
        matched = true;
        var n = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        total += m.Groups["u"].Value switch { "ms" => n / 1000, "h" => n * 3600, "m" => n * 60, _ => n };
    }
    return matched ? TimeSpan.FromSeconds(total) : null;
}

sealed class Options
{
    public string Suite { get; private set; } = "smoke";
    public List<string> ScenarioIds { get; } = new();
    public bool List { get; private set; }
    public bool FixturesOnly { get; private set; }
    public bool Help { get; private set; }
    public string? OutDir { get; private set; }
    public int MaxLlmRequestsPerCase { get; private set; } = 6;
    public int MinRemainingTokens { get; private set; } = 5000;

    public const string Usage = """
        FitBot NON-RAG evaluator (production C# path, model openai/gpt-oss-120b)

          --suite smoke|full              smoke = 5 representative cases (default), full = all catalog cases
          --scenarios id1,id2             run specific scenario ids
          --list                          list the scenario catalog
          --fixtures-only                 seed a fixture DB and print the Completed/Planned view (no LLM calls)
          --out <dir>                     output root (default: <repo>/evaluation/runs)
          --max-llm-requests-per-case <n> safety cap against unexpected tool loops (default 6)
          --min-remaining-tokens <n>      wait for the TPM window between cases below this (default 5000)

        API key: GROQ_API_KEY env var, otherwise Groq:ApiKey from FitTracker.API user-secrets. Never logged.
        """;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
            switch (args[i])
            {
                case "--suite": o.Suite = Next(); break;
                case "--scenarios": o.ScenarioIds.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "--list": o.List = true; break;
                case "--fixtures-only": o.FixturesOnly = true; break;
                case "--out": o.OutDir = Next(); break;
                case "--max-llm-requests-per-case": o.MaxLlmRequestsPerCase = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--min-remaining-tokens": o.MinRemainingTokens = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--help" or "-h": o.Help = true; break;
                default: throw new ArgumentException($"Unknown argument '{args[i]}'. Use --help.");
            }
        }
        return o;
    }
}
