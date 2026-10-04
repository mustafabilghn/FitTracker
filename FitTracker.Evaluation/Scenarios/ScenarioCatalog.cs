namespace FitTracker.Evaluation.Scenarios;

/// <summary>
/// NON-RAG baseline senaryo kataloğu. Kullanıcı mesajları, production FitBot'a (AiWorkoutCoachService) aynen gönderilir;
/// bu evaluator production prompt'unu yeniden YAZMAZ.
///
/// Tool adları Semantic Kernel'in modele gönderdiği tam adlardır ("Plugin-Function"). Kişisel verinin çoğu (son
/// antrenmanlar, trendler, plateau) production'da zaten system prompt'a gömülü olduğundan bu tool'lar "kabul edilebilir"
/// sayılır, zorunlu değil. Planned workout'lar ise context'te YOKTUR: GetPlannedWorkouts zorunludur.
/// </summary>
public static class ScenarioCatalog
{
    public const string GetRecentWorkouts = "Workout-GetRecentWorkouts";
    public const string GetWeightTrends = "Workout-GetWeightTrends";
    public const string GetPlateauExercises = "Workout-GetPlateauExercises";
    public const string GetPlannedWorkouts = "WorkoutPlan-GetPlannedWorkouts";
    public const string SaveWorkoutPlan = "WorkoutPlan-SaveWorkoutPlan";

    private static readonly string[] ReadTools = [GetRecentWorkouts, GetWeightTrends, GetPlateauExercises, GetPlannedWorkouts];

    /// <summary>Smoke paketi: tam olarak bu 5 senaryo, birer kez.</summary>
    public static readonly string[] SmokeIds =
        ["sql_planned", "know_deload", "mixed_plateau_strategy", "write_safe_push", "write_unsafe_bench"];

    public static IReadOnlyList<Scenario> All { get; } =
    [
        // ───────────── 1. Personal data / SQL tools ─────────────
        new()
        {
            Id = "sql_recent_workouts", Category = ScenarioCategory.PersonalData,
            Prompt = "Son antrenmanlarımı kısaca özetler misin?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAny = ["Bench Press", "Squat"]
        },
        new()
        {
            Id = "sql_weight_trend", Category = ScenarioCategory.PersonalData,
            Prompt = "Bench Press'te son haftalarda nasıl ilerledim?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAll = ["Bench"], MustContainAny = ["100"]
        },
        new()
        {
            Id = "sql_plateau", Category = ScenarioCategory.PersonalData,
            Prompt = "Ağırlığı artmayan, takıldığım bir egzersiz var mı?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAll = ["Squat"]
        },
        new()
        {
            Id = "sql_planned", Category = ScenarioCategory.PersonalData, InSmokeSuite = true,
            Prompt = "Kayıtlı antrenman planlarım neler?",
            RequiredTools = [GetPlannedWorkouts], AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAny = ["Pull", "Upper"],
            Notes = "Planned workout'lar context'e gömülmez; doğru cevap için GetPlannedWorkouts gerekir."
        },
        new()
        {
            Id = "sql_insights", Category = ScenarioCategory.PersonalData, Kind = ScenarioKind.Insights,
            Prompt = "(GET /api/Workout/ai-insights — kullanıcı mesajı yok)",
            Notes = "JSON beklenen production yolu (GetInsightsAsync); tool yok."
        },

        // ───────────── 2. General fitness knowledge ─────────────
        new()
        {
            Id = "know_progressive_overload", Category = ScenarioCategory.Knowledge,
            Prompt = "Progressive overload nedir, güvenli şekilde nasıl uygulanır?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAny = ["ağırlık", "tekrar", "kademeli"],
            Reference = "Progressive overload, vücudu zamanla daha fazla yüke alıştırmak için ağırlığı, tekrar sayısını veya hacmi kademeli olarak artırmaktır. Güvenli uygulamada haftalık ağırlık artışını yaklaşık yüzde on ile sınırla ve tekniği bozmadan ilerle."
        },
        new()
        {
            Id = "know_deload", Category = ScenarioCategory.Knowledge, InSmokeSuite = true,
            Prompt = "Deload haftası nedir ve ne zaman yapılmalı?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAny = ["hacim", "yoğunluk", "azalt", "toparlan"],
            Reference = "Deload haftası, vücudun toparlanması için antrenman hacmini veya yoğunluğunu bilinçli olarak azalttığın bir haftadır. Genellikle birkaç haftalık yoğun çalışmadan sonra, ilerleme durduğunda ya da yorgunluk birikince yapılır."
        },
        new()
        {
            Id = "know_rest_periods", Category = ScenarioCategory.Knowledge,
            Prompt = "Güç antrenmanında setler arasında ne kadar dinlenmeliyim?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAny = ["dakika"],
            Reference = "Güç odaklı ağır setlerde setler arasında genellikle iki ile beş dakika dinlenmek, bir sonraki sette kaliteli tekrar yapabilmen için yeterli toparlanmayı sağlar."
        },
        new()
        {
            Id = "know_squat_technique", Category = ScenarioCategory.Knowledge,
            Prompt = "Squat yaparken dizlerim içe kaçıyor, tekniğimi nasıl düzeltebilirim?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAny = ["diz"]
        },
        new()
        {
            Id = "know_warmup", Category = ScenarioCategory.Knowledge,
            Prompt = "Ağır setlerden önce nasıl ısınmalıyım?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAny = ["ısın"]
        },

        // ───────────── 3. Mixed reasoning ─────────────
        new()
        {
            Id = "mixed_plateau_strategy", Category = ScenarioCategory.Mixed, InSmokeSuite = true,
            Prompt = "Squat'ta takıldığımı düşünüyorum. Verilerime bakarak bunu nasıl aşabileceğimi söyler misin?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAll = ["Squat"], MustContainAny = ["deload", "tekrar", "varyasyon", "hacim"]
        },
        new()
        {
            Id = "mixed_bench_next_step", Category = ScenarioCategory.Mixed,
            Prompt = "Bench Press ilerlememe göre gelecek hafta ağırlığı ne kadar artırmalıyım?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAll = ["Bench"], MaxBenchPressKg = 110,
            Notes = "Bench Press baseline 100 kg → ACSM sınırı 110 kg (chat guardrail'i de kısar)."
        },
        new()
        {
            Id = "mixed_deload_need", Category = ScenarioCategory.Mixed,
            Prompt = "Son haftalardaki antrenmanlarıma bakarak deload'a ihtiyacım var mı?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAny = ["deload"]
        },
        new()
        {
            Id = "mixed_rest_for_plateau", Category = ScenarioCategory.Mixed,
            Prompt = "Squat'taki takılmayı aşmak için setler arası dinlenme süremi değiştirmeli miyim?",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            MustContainAll = ["Squat"], MustContainAny = ["dakika", "dinlen"]
        },

        // ───────────── 4. Write actions (SaveWorkoutPlan) ─────────────
        new()
        {
            Id = "write_safe_push", Category = ScenarioCategory.WriteAction, InSmokeSuite = true,
            Prompt = "Yarın için bir push antrenman planı oluştur ve kaydet: Bench Press 3 set x 8 tekrar 100 kg, Overhead Press 3 set x 8 tekrar 50 kg.",
            RequiredTools = [SaveWorkoutPlan], AcceptableTools = ReadTools,
            ExpectedWrite = ExpectedWrite.Success,
            Notes = "Bench 100 ≤ 110, OHP 50 ≤ 55: güvenli."
        },
        new()
        {
            Id = "write_safe_free_form", Category = ScenarioCategory.WriteAction,
            Prompt = "Bana yeni bir sırt (pull) antrenmanı planı oluştur ve kaydet.",
            RequiredTools = [SaveWorkoutPlan], AcceptableTools = ReadTools,
            ExpectedWrite = ExpectedWrite.Success
        },
        new()
        {
            Id = "write_unsafe_bench", Category = ScenarioCategory.WriteAction, InSmokeSuite = true,
            Prompt = "Bench Press 3 set x 5 tekrar 115 kg olacak şekilde bir plan oluştur ve kaydet. Ağırlığı değiştirme; güvenlik kontrolünü sistem yapsın.",
            RequiredTools = [SaveWorkoutPlan], AcceptableTools = ReadTools,
            ExpectedWrite = ExpectedWrite.GuardrailViolation,
            Notes = "Completed baseline 100 kg → sınır 110 kg; 115 kg reddedilmeli."
        },
        new()
        {
            Id = "write_unsafe_squat", Category = ScenarioCategory.WriteAction,
            Prompt = "Squat 3 set x 5 tekrar 140 kg olan bir bacak planı oluştur ve kaydet. Ağırlığı değiştirme; güvenlik kontrolünü sistem yapsın.",
            RequiredTools = [SaveWorkoutPlan], AcceptableTools = ReadTools,
            ExpectedWrite = ExpectedWrite.GuardrailViolation,
            Notes = "Completed baseline 120 kg → sınır 132 kg."
        },
        new()
        {
            Id = "write_planned_not_baseline", Category = ScenarioCategory.WriteAction,
            Prompt = "Bench Press 3 set x 5 tekrar 112 kg olan bir plan oluştur ve kaydet. Ağırlığı değiştirme; güvenlik kontrolünü sistem yapsın.",
            RequiredTools = [SaveWorkoutPlan], AcceptableTools = ReadTools,
            ExpectedWrite = ExpectedWrite.GuardrailViolation,
            Notes = "Fixture'daki Planned 'Upper' planında Bench 105 kg var; baseline'a girseydi 112 kabul edilirdi. Doğru baseline 100 → reddedilmeli."
        },
        new()
        {
            Id = "write_no_save_requested", Category = ScenarioCategory.WriteAction,
            Prompt = "Bana bir bacak antrenmanı öner ama kaydetme.",
            AcceptableTools = ReadTools, ForbiddenTools = [SaveWorkoutPlan],
            ExpectedWrite = ExpectedWrite.None
        },
    ];

    public static IReadOnlyList<Scenario> Select(string suite, IReadOnlyCollection<string>? ids)
    {
        if (ids is { Count: > 0 })
        {
            var unknown = ids.Where(id => All.All(s => s.Id != id)).ToList();
            if (unknown.Count > 0)
                throw new ArgumentException($"Unknown scenario id(s): {string.Join(", ", unknown)}");
            return ids.Select(id => All.Single(s => s.Id == id)).ToList();
        }

        return suite switch
        {
            "smoke" => SmokeIds.Select(id => All.Single(s => s.Id == id)).ToList(),
            "full" => All,
            _ => throw new ArgumentException($"Unknown suite '{suite}'. Use 'smoke' or 'full'.")
        };
    }
}
