using FitTracker.Evaluation.Instrumentation;

namespace FitTracker.Evaluation.Evaluation;

public sealed record PlannedWorkoutSnapshot(string Name, string Date, IReadOnlyList<string> Exercises);

/// <summary>Fixture veritabanının özet durumu (case öncesi/sonrası).</summary>
public sealed record DbSnapshot(
    int PrimaryCompleted,
    int PrimaryPlanned,
    int OtherCompleted,
    int OtherPlanned,
    int TotalExercises,
    int TotalSets,
    IReadOnlyList<PlannedWorkoutSnapshot> PrimaryPlannedWorkouts);

public sealed record CheckResult(string Name, bool Required, bool Passed, string Detail);

public sealed record SaveOutcomeSnapshot(bool Success, string? WorkoutName, string? Reason, string? Detail, string? Exercise, double? LimitKg, int ExerciseCount, int SetCount);

/// <summary>Tek bir case'in tüm kaydı (cases.jsonl'deki bir satır).</summary>
public sealed class CaseResult
{
    // Kimlik
    public required string ScenarioId { get; init; }
    public required string Category { get; init; }
    public required string Kind { get; init; }
    public required string ActionType { get; init; }
    public required string Prompt { get; init; }

    // Çalıştırma metadata'sı
    public required string ConfiguredModel { get; init; }
    public IReadOnlyList<string> ObservedRequestModels { get; set; } = [];
    public IReadOnlyList<string> ObservedResponseModels { get; set; } = [];
    public required string GitCommitSha { get; init; }
    public required bool WorkingTreeDirty { get; init; }
    public required bool ProductionSourceDirty { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Production'ın ilk istekte gerçekte gönderdiği üretim ayarları (temperature vb.).</summary>
    public Dictionary<string, object?> GenerationSettings { get; set; } = new();

    // Sonuç
    public string FinalResponse { get; set; } = string.Empty;
    public bool? GuardrailTriggered { get; set; }
    public IReadOnlyList<string> InterceptedProgressions { get; set; } = [];
    public IReadOnlyList<string> PlateauAlerts { get; set; } = [];

    // Hata / hız
    public string? Error { get; set; }
    public bool RateLimited429 { get; set; }
    public long LatencyMs { get; set; }

    // LLM
    public int LlmRequestCount { get; set; }
    public IReadOnlyList<LlmCallRecord> LlmCalls { get; set; } = [];
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }

    // Tool'lar
    public IReadOnlyList<ToolCallRequest> ToolCallsRequestedByModel { get; set; } = [];
    public IReadOnlyList<ToolInvocationRecord> ToolInvocations { get; set; } = [];

    // Write / DB
    public SaveOutcomeSnapshot? SaveWorkoutPlan { get; set; }
    public DbSnapshot? DbBefore { get; set; }
    public DbSnapshot? DbAfter { get; set; }
    public IReadOnlyList<PlannedWorkoutSnapshot> CreatedPlannedWorkouts { get; set; } = [];

    // Doğrulama
    public IReadOnlyList<CheckResult> Checks { get; set; } = [];
    public bool TaskSuccess { get; set; }
    public bool CorrectToolSelection { get; set; }
    public bool? ToolExecutionSuccess { get; set; }
    public bool? WriteCorrect { get; set; }
    public bool? GuardrailCorrect { get; set; }

    /// <summary>
    /// Güvensiz plan senaryolarında planın NASIL engellendiği (ayrı raporlanır, guardrail başarısıyla karıştırılmaz):
    /// server_guardrail_rejection | model_pre_tool_refusal | unsafe_plan_persisted | other.
    /// </summary>
    public string? UnsafeWriteHandling { get; set; }

    /// <summary>Yol fark etmeksizin güvensiz plan DB'ye yazılmadı mı.</summary>
    public bool? UnsafeWriteBlocked { get; set; }
    public bool FalseSaveClaim { get; set; }
    public bool CrossUserLeak { get; set; }
    public bool ModelSentIdentityLikeArgument { get; set; }
    public bool? SecondCompletionAfterWrite { get; set; }
    public bool? DeterministicWriteReply { get; set; }
    public bool LanguageOk { get; set; }
    public IReadOnlyList<string> ForbiddenPatterns { get; set; } = [];
    public bool? JsonValid { get; set; }
    public double? RougeL { get; set; }
    public string? Reference { get; set; }
}
