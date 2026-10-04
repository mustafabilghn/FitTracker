namespace FitTracker.Evaluation.Scenarios;

public enum ScenarioCategory
{
    /// <summary>Kişisel veri: SQL + WorkoutPlugin / WorkoutPlan okuma tool'ları (veya context injection).</summary>
    PersonalData,
    /// <summary>Genel fitness bilgisi: kişisel veri gerektirmez (ileride RAG'in hedefi).</summary>
    Knowledge,
    /// <summary>Kişisel veri + genel bilgi birlikte gerekir.</summary>
    Mixed,
    /// <summary>Write aksiyonu: SaveWorkoutPlan.</summary>
    WriteAction
}

public enum ScenarioKind
{
    /// <summary>POST /api/Workout/fitbot/chat ile aynı yol: AiWorkoutCoachService.ChatAsync.</summary>
    Chat,
    /// <summary>GET /api/Workout/ai-insights ile aynı yol: AiWorkoutCoachService.GetInsightsAsync (JSON beklenir).</summary>
    Insights
}

public enum ExpectedWrite
{
    /// <summary>Kayıt beklenmez (SaveWorkoutPlan çağrılırsa planned workout oluşmamalı).</summary>
    None,
    /// <summary>SaveWorkoutPlan başarılı olmalı: +1 Planned workout.</summary>
    Success,
    /// <summary>SaveWorkoutPlan guardrail tarafından reddedilmeli: DB değişmemeli.</summary>
    GuardrailViolation
}

/// <summary>
/// Bir değerlendirme senaryosu. Beklentiler bilinçli olarak gevşek ve davranış odaklıdır: açık uçlu koçluk metni
/// tek bir doğru cevaba sahip değildir; burada yalnızca deterministik olarak doğrulanabilen şeyler (tool kullanımı,
/// DB etkisi, guardrail, kritik olgular, sızıntı, dil, yanlış kayıt iddiası) kontrol edilir.
/// </summary>
public sealed class Scenario
{
    public required string Id { get; init; }
    public required ScenarioCategory Category { get; init; }
    public required string Prompt { get; init; }
    public ScenarioKind Kind { get; init; } = ScenarioKind.Chat;
    public string ActionType { get; init; } = "free";

    /// <summary>Hepsi çağrılmalı (ör. Planned workout'lar context'te YOK → GetPlannedWorkouts zorunlu).</summary>
    public string[] RequiredTools { get; init; } = [];

    /// <summary>Çağrılması kabul edilebilir ama zorunlu değil (veri zaten context'te olabilir).</summary>
    public string[] AcceptableTools { get; init; } = [];

    /// <summary>Asla çağrılmamalı (ör. kullanıcı "kaydetme" dediğinde SaveWorkoutPlan).</summary>
    public string[] ForbiddenTools { get; init; } = [];

    public ExpectedWrite ExpectedWrite { get; init; } = ExpectedWrite.None;

    /// <summary>Final cevapta hepsi geçmeli (büyük/küçük harf duyarsız).</summary>
    public string[] MustContainAll { get; init; } = [];

    /// <summary>Final cevapta en az biri geçmeli (kaba içerik sezgisi).</summary>
    public string[] MustContainAny { get; init; } = [];

    /// <summary>Final cevapta hiçbiri geçmemeli.</summary>
    public string[] MustNotContain { get; init; } = [];

    /// <summary>Cevaptaki Bench Press ağırlıkları bu değeri aşmamalı (ACSM: baseline 100 kg → 110 kg).</summary>
    public double? MaxBenchPressKg { get; init; }

    /// <summary>İsteğe bağlı, production kurallarıyla uyumlu kurgu referans (yalnızca ikincil BLEU/ROUGE için).</summary>
    public string? Reference { get; init; }

    public bool InSmokeSuite { get; init; }
    public string? Notes { get; init; }
}
