using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FitTrackr.API.Data;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Plugins;
using FitTrackr.API.Repositories;
using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using FitTrackr.API.Validations;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// Phase 3: FitBot'un SaveWorkoutPlan write aksiyonu. Production DI kayıtları + gerçek WorkoutRepository +
/// gerçek WorkoutAnalysisService + SQLite in-memory (gerçek transaction semantiği) kullanılır. LLM transport'u sahte;
/// gerçek Groq API key'i gerekmez, tüm girdiler deterministiktir.
///
/// Seed (her test için temiz DB):
///   User A: Bench Press 95 → 100 kg, Squat 120 kg  (baseline: Bench 100, Squat 120)
///   User B: Bench Press 200 kg, Squat 250 kg
/// </summary>
public class WorkoutPlanPersistenceTests
{
    private const string UserA = "user-A";
    private const string UserB = "user-B";

    // ───────────────────── A. SAFE PLAN ─────────────────────

    [Fact]
    public async Task A_SafePlan_IsPersistedForCurrentUser_WithFullGraph()
    {
        using var h = Harness.Create();

        var result = await h.SaveAsync(UserA, "Push", Ex("Bench Press", 105, 105, 105), Ex("Squat", 130, 130, 130));

        Assert.True(result.Success, result.Reason + " " + result.Detail);
        Assert.NotNull(result.WorkoutId);

        var saved = await h.LoadWorkoutAsync(result.WorkoutId!.Value);
        Assert.Equal(UserA, saved.userId);
        Assert.Equal("Push", saved.WorkoutName);
        Assert.Equal(DateTime.UtcNow.Date, saved.WorkoutDate.Date);
        Assert.Equal(new[] { "Bench Press", "Squat" }, saved.Exercises.Select(e => e.ExerciseName).OrderBy(n => n).ToArray());
        Assert.All(saved.Exercises, e => Assert.Equal("Medium", e.Intensity.Level)); // seed referans verisi
        Assert.Equal(6, saved.Exercises.Sum(e => e.ExerciseSets.Count));

        var bench = saved.Exercises.Single(e => e.ExerciseName == "Bench Press");
        Assert.Equal(new[] { 1, 2, 3 }, bench.ExerciseSets.Select(s => s.SetNumber).OrderBy(n => n).ToArray());
        Assert.All(bench.ExerciseSets, s => { Assert.Equal(105, s.WeightInKg); Assert.Equal("8", s.Reps); });
        Assert.All(saved.Exercises.Single(e => e.ExerciseName == "Squat").ExerciseSets, s => Assert.Equal(130, s.WeightInKg));

        Assert.Equal((Workouts: 1, Exercises: 2, Sets: 6), await h.NewRowsAsync());
    }

    // ───────────────────── B / C. EXACT 10% vs OVER 10% ─────────────────────

    [Fact]
    public async Task B_Exactly10PercentOverBaseline_IsSaved()
    {
        using var h = Harness.Create();

        var result = await h.SaveAsync(UserA, "Push", Ex("Bench Press", 110));

        Assert.True(result.Success);
        Assert.Equal((1, 1, 1), await h.NewRowsAsync());
    }

    [Theory]
    [InlineData(111)]
    [InlineData(110.01)]
    public async Task C_MoreThan10PercentOverBaseline_IsRejected_AndDbUnchanged(double weight)
    {
        using var h = Harness.Create();

        var result = await h.SaveAsync(UserA, "Push", Ex("Bench Press", 100, weight));

        Assert.False(result.Success);
        Assert.Equal("guardrail_violation", result.Reason);
        Assert.Equal("Bench Press", result.Exercise);
        Assert.Equal(110, result.LimitKg);
        Assert.Null(result.WorkoutId);
        Assert.Equal((0, 0, 0), await h.NewRowsAsync());
    }

    [Fact]
    public void C_SharedRule_IsDeterministic_AtTheExact10PercentBoundary_ForRealisticWeights()
    {
        // Tek numerik kural (AcsmProgressionRule) hem chat hem kayıt için kullanılır. 0.5 kg adımlarla 0.5–400 kg arası
        // tüm baseline'larda tam %10 güvenli, +0.01 kg güvensiz olmalı (kayan nokta kenar durumu yok).
        for (var b = 0.5m; b <= 400m; b += 0.5m)
        {
            var baseline = (double)b;
            var exactly10 = (double)(b * 1.10m);
            Assert.True(AcsmProgressionRule.IsWithinLimit(exactly10, baseline), $"baseline {b}: {exactly10} should be SAFE");
            Assert.False(AcsmProgressionRule.IsWithinLimit(exactly10 + 0.01, baseline), $"baseline {b}: {exactly10 + 0.01} should be UNSAFE");
        }
    }

    // ───────────────────── D / E. OWN BASELINES, GAP4 NOT CARRIED OVER ─────────────────────

    [Fact]
    public async Task D_EachExerciseIsCheckedAgainstItsOwnBaseline()
    {
        using var h = Harness.Create();

        // Squat 130 > Bench'in sınırı (110) ama Squat'ın kendi sınırı 132 → SAFE.
        var safe = await h.SaveAsync(UserA, "Mix", Ex("Bench Press", 105), Ex("Squat", 130));
        Assert.True(safe.Success);

        // Squat 133 > kendi sınırı 132 → yalnızca Squat ihlal eder ve sınır Squat'ın kendi baseline'ından (120) gelir.
        using var h2 = Harness.Create();
        var unsafeSquat = await h2.SaveAsync(UserA, "Mix", Ex("Bench Press", 105), Ex("Squat", 133));
        Assert.False(unsafeSquat.Success);
        Assert.Equal("Squat", unsafeSquat.Exercise);
        Assert.Equal(132, unsafeSquat.LimitKg);
        Assert.Equal((0, 0, 0), await h2.NewRowsAsync());
    }

    [Fact]
    public async Task E_Gap4TextLimitation_StillExistsInChat_ButIsNotCarriedIntoStructuredPersistence()
    {
        using var h = Harness.Create();
        var context = await h.ContextForAsync(UserA);

        // Chat guardrail (değiştirilmedi): aynı satırdaki Squat 130, Bench'in 110 sınırına yanlışlıkla kısılır (Gap4).
        var chat = new AcsmGuardrailService().Validate("Bugün Bench Press 105 kg ve Squat 130 kg deneyebilirsin.", context);
        Assert.True(chat.Triggered);
        Assert.Contains("Squat 110.0 kg", chat.SanitizedReply);

        // Aynı öneri yapılandırılmış planla: Squat kendi baseline'ı (120 → 132) ile değerlendirilir ve aynen kaydedilir.
        var result = await h.SaveAsync(UserA, "Mix", Ex("Bench Press", 105), Ex("Squat", 130));
        Assert.True(result.Success);
        var saved = await h.LoadWorkoutAsync(result.WorkoutId!.Value);
        Assert.All(saved.Exercises.Single(e => e.ExerciseName == "Squat").ExerciseSets, s => Assert.Equal(130, s.WeightInKg));
    }

    // ───────────────────── F. ATOMICITY ─────────────────────

    [Fact]
    public async Task F_OneUnsafeExercise_RejectsTheWholePlan_NoPartialSave()
    {
        using var h = Harness.Create();

        var result = await h.SaveAsync(UserA, "Full", Ex("Bench Press", 105), Ex("Squat", 140), Ex("Deadlift", 140));

        Assert.False(result.Success);
        Assert.Equal("guardrail_violation", result.Reason);
        Assert.Equal("Squat", result.Exercise);
        Assert.Equal((0, 0, 0), await h.NewRowsAsync());
    }

    [Fact]
    public async Task F_DatabaseFailureMidInsert_RollsBackEverything_AndLeavesNoTrackedLeftovers()
    {
        using var h = Harness.Create();
        h.Interceptor.FailOnInsertInto = "ExerciseSets"; // Workout ve Exercise INSERT'leri çalıştıktan sonra patlar

        using var scope = h.Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(UserA);

        var failed = await Harness.InvokeSaveAsync(scope, "Push", new() { Ex("Bench Press", 105, 105) });

        Assert.False(failed.Success);
        Assert.Equal("save_failed", failed.Reason);
        Assert.Null(failed.Detail); // altyapı/SQL detayı sızmaz
        Assert.Contains(h.Interceptor.ExecutedInserts, t => t == "Workouts"); // gerçekten kısmi yazım başlamıştı...
        Assert.Equal((0, 0, 0), await h.NewRowsAsync());                      // ...ve transaction hepsini geri aldı

        // Aynı scope'ta düzeltilmiş tekrar deneme: başarısız graf change tracker'dan çıkarıldığı için tekrar yazılmaz.
        h.Interceptor.FailOnInsertInto = null;
        var retry = await Harness.InvokeSaveAsync(scope, "Pull", new() { Ex("Barbell Row", 60) });
        Assert.True(retry.Success);
        Assert.Equal((1, 1, 1), await h.NewRowsAsync());
        Assert.Equal("Pull", (await h.LoadWorkoutAsync(retry.WorkoutId!.Value)).WorkoutName);
    }

    // ───────────────────── G / H / K. CURRENT USER ─────────────────────

    [Fact]
    public async Task G_ModelSuppliedUserId_IsNotPartOfTheContract_AndRecordIsCreatedOnlyForCurrentUser()
    {
        using var h = Harness.Create();
        using var scope = h.Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(UserA);
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        var function = kernel.Plugins[WorkoutPlanPlugin.PluginName]["SaveWorkoutPlan"];
        Assert.Equal(new[] { "workoutName", "exercises", "workoutDate" }, function.Metadata.Parameters.Select(p => p.Name).ToArray());

        var result = (await kernel.InvokeAsync(function, new KernelArguments
        {
            ["workoutName"] = "Push",
            ["exercises"] = new List<WorkoutPlanExerciseDto> { Ex("Bench Press", 105) },
            ["userId"] = UserB // kötü niyetli ek argüman: sözleşmede yok, bağlanmaz
        })).GetValue<SaveWorkoutPlanResult>()!;

        Assert.True(result.Success);
        Assert.Equal(UserA, (await h.LoadWorkoutAsync(result.WorkoutId!.Value)).userId);
        Assert.Equal(0, await h.NewWorkoutsForAsync(UserB));
    }

    [Fact]
    public async Task H_NoCurrentUser_FailsClosed_AndDbUnchanged()
    {
        using var h = Harness.Create();

        var result = await h.SaveAsync(userId: null, "Push", Ex("Bench Press", 105));

        Assert.False(result.Success);
        Assert.Equal("no_current_user", result.Reason);
        Assert.Equal((0, 0, 0), await h.NewRowsAsync());
    }

    [Fact]
    public async Task K_BaselineComesOnlyFromCurrentUsersHistory()
    {
        using var h = Harness.Create();

        // B'nin Bench baseline'ı 200 (sınır 220). A için 150 kg; B'nin baseline'ı kullanılsaydı geçerdi.
        var forA = await h.SaveAsync(UserA, "Push", Ex("Bench Press", 150));
        Assert.False(forA.Success);
        Assert.Equal(110, forA.LimitKg); // A'nın kendi baseline'ı: 100

        // B için 210 kg (kendi sınırı 220) kaydedilir; A'nın düşük baseline'ı B'yi de etkilemez.
        var forB = await h.SaveAsync(UserB, "Push", Ex("Bench Press", 210));
        Assert.True(forB.Success);
        Assert.Equal(UserB, (await h.LoadWorkoutAsync(forB.WorkoutId!.Value)).userId);
        Assert.Equal(0, await h.NewWorkoutsForAsync(UserA));
    }

    // ───────────────────── I. DUPLICATE SAVE ─────────────────────

    [Fact]
    public async Task I_SecondSaveInSameRequestScope_IsRejected_NoDuplicatePlan()
    {
        using var h = Harness.Create();
        using var scope = h.Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(UserA);

        var first = await Harness.InvokeSaveAsync(scope, "Push", new() { Ex("Bench Press", 105) });
        var second = await Harness.InvokeSaveAsync(scope, "Push", new() { Ex("Bench Press", 105) });

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal("duplicate_save", second.Reason);
        Assert.Equal((1, 1, 1), await h.NewRowsAsync());

        // Yeni bir request scope'u (yeni sohbet isteği) tekrar kaydedebilir.
        var nextRequest = await h.SaveAsync(UserA, "Pull", Ex("Barbell Row", 60));
        Assert.True(nextRequest.Success);
    }

    [Fact]
    public async Task I_RejectedAttempt_DoesNotBlockACorrectedRetryInSameScope()
    {
        using var h = Harness.Create();
        using var scope = h.Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(UserA);

        var rejected = await Harness.InvokeSaveAsync(scope, "Push", new() { Ex("Bench Press", 150) });
        var corrected = await Harness.InvokeSaveAsync(scope, "Push", new() { Ex("Bench Press", 110) });

        Assert.Equal("guardrail_violation", rejected.Reason);
        Assert.True(corrected.Success);
        Assert.Equal((1, 1, 1), await h.NewRowsAsync());
    }

    // ───────────────────── J. MALFORMED INPUT ─────────────────────

    public static TheoryData<string, string?, string?, string> MalformedPlans => new()
    {
        // case, workoutName, workoutDate, expected detail   (egzersizler BuildMalformed ile üretilir)
        { "empty_workout_name", "", null, "invalid_workout_name" },
        { "too_long_workout_name", new string('x', 21), null, "invalid_workout_name" },
        { "null_exercises", "Push", null, "exercises_required" },
        { "empty_exercises", "Push", null, "exercises_required" },
        { "too_many_exercises", "Push", null, "too_many_exercises" },
        { "empty_exercise_name", "Push", null, "invalid_exercise_name" },
        { "too_long_exercise_name", "Push", null, "invalid_exercise_name" },
        { "duplicate_exercise", "Push", null, "duplicate_exercise" },
        { "null_sets", "Push", null, "sets_required" },
        { "empty_sets", "Push", null, "sets_required" },
        { "too_many_sets", "Push", null, "too_many_sets" },
        { "null_set_entry", "Push", null, "invalid_set" },
        { "zero_reps", "Push", null, "invalid_reps" },
        { "too_many_reps", "Push", null, "invalid_reps" },
        { "negative_weight", "Push", null, "invalid_weight" },
        { "absurd_weight", "Push", null, "invalid_weight" },
        { "nan_weight", "Push", null, "invalid_weight" },
        { "three_decimal_weight", "Push", null, "invalid_weight" },
        { "bad_date_format", "Push", "05/10/2026", "invalid_date" },
        { "date_in_past", "Push", DateTime.UtcNow.Date.AddDays(-10).ToString("yyyy-MM-dd"), "date_out_of_range" },
        { "date_far_future", "Push", DateTime.UtcNow.Date.AddDays(60).ToString("yyyy-MM-dd"), "date_out_of_range" },
    };

    [Theory]
    [MemberData(nameof(MalformedPlans))]
    public async Task J_MalformedPlan_IsRejected_AndDbUnchanged(string caseName, string? workoutName, string? workoutDate, string expectedDetail)
    {
        using var h = Harness.Create();

        var result = await h.SaveAsync(UserA, workoutName, BuildMalformed(caseName), workoutDate);

        Assert.False(result.Success);
        Assert.Equal("invalid_plan", result.Reason);
        Assert.Equal(expectedDetail, result.Detail);
        Assert.Equal((0, 0, 0), await h.NewRowsAsync());
    }

    private static List<WorkoutPlanExerciseDto>? BuildMalformed(string caseName)
    {
        WorkoutPlanExerciseDto WithSets(params (int reps, double kg)[] sets) => new()
        {
            ExerciseName = "Bench Press",
            Sets = sets.Select(s => new WorkoutPlanSetDto { Reps = s.reps, WeightInKg = s.kg }).ToList()
        };

        return caseName switch
        {
            "null_exercises" => null,
            "empty_exercises" => new(),
            "too_many_exercises" => Enumerable.Range(1, WorkoutPlanValidator.MaxExercises + 1).Select(i => Ex($"Exercise {i}", 20)).ToList(),
            "empty_exercise_name" => new() { Ex("   ", 50) },
            "too_long_exercise_name" => new() { Ex(new string('y', 51), 50) },
            "duplicate_exercise" => new() { Ex("Bench Press", 100), Ex("bench  press", 100) },
            "null_sets" => new() { new WorkoutPlanExerciseDto { ExerciseName = "Bench Press", Sets = null } },
            "empty_sets" => new() { new WorkoutPlanExerciseDto { ExerciseName = "Bench Press", Sets = new() } },
            "too_many_sets" => new() { Ex("Bench Press", Enumerable.Repeat(100.0, WorkoutPlanValidator.MaxSetsPerExercise + 1).ToArray()) },
            "null_set_entry" => new() { new WorkoutPlanExerciseDto { ExerciseName = "Bench Press", Sets = new() { null! } } },
            "zero_reps" => new() { WithSets((0, 100)) },
            "too_many_reps" => new() { WithSets((101, 100)) },
            "negative_weight" => new() { WithSets((8, -5)) },
            "absurd_weight" => new() { WithSets((8, 500.01)) },
            "nan_weight" => new() { WithSets((8, double.NaN)) },
            "three_decimal_weight" => new() { WithSets((8, 100.005)) },
            _ => new() { Ex("Bench Press", 100) } // geçerli egzersiz; hata workout adı/tarihinde
        };
    }

    // ───────────────────── ORCHESTRATION (hardened write lifecycle) ─────────────────────
    // model → SaveWorkoutPlan → validation → guardrail → repository → DB → SERVER-AUTHORITATIVE reply.
    // Write aksiyonundan sonra LLM'e ikinci kez GİDİLMEZ (429 riski yok, model sonucu çarpıtamaz).

    // Model sözleşmesine uygun plan: setNumber yok. Model kötü niyetli olarak userId eklemeye çalışıyor.
    private const string PushPlanJson = """
        {"userId":"user-B","workoutName":"Push","exercises":[
          {"exerciseName":"Bench Press","userId":"user-B","sets":[{"reps":8,"weightInKg":105},{"reps":8,"weightInKg":105}]},
          {"exerciseName":"Squat","sets":[{"reps":5,"weightInKg":130}]}]}
        """;

    private const string UnsafeLegsPlanJson = """
        {"workoutName":"Legs","exercises":[{"exerciseName":"Squat","sets":[{"reps":5,"weightInKg":150},{"reps":5,"weightInKg":140}]}]}
        """;

    [Fact]
    public async Task Hardening1_SuccessfulSave_NeverCallsTheLlmASecondTime_EvenIfACompletionIsAvailable()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueToolCall("call_save", "WorkoutPlan-SaveWorkoutPlan", PushPlanJson);
        h.Handler.EnqueueReply("LLM-FINAL: kaydettim!"); // ikinci completion hazır; kullanılmamalı

        var response = await h.ChatAsync(UserA, "Bana bir push antrenmanı oluştur ve kaydet.");

        Assert.Single(h.Handler.Requests);              // yalnızca 1 LLM çağrısı
        Assert.Equal(1, h.Handler.PendingResponses);    // hazırdaki ikinci completion hiç tüketilmedi
        Assert.DoesNotContain("LLM-FINAL", response.Reply);
    }

    [Fact]
    public async Task Hardening2_GuardrailRejection_NeverCallsTheLlmForTheFinalAnswer()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueToolCall("call_save", "WorkoutPlan-SaveWorkoutPlan", UnsafeLegsPlanJson);
        h.Handler.EnqueueReply("Planını kaydettim!"); // modelin yalan başarı beyanı: asla kullanıcıya ulaşmamalı

        var response = await h.ChatAsync(UserA, "Bana bacak antrenmanı oluştur ve kaydet.");

        Assert.Single(h.Handler.Requests);
        Assert.Equal(1, h.Handler.PendingResponses);
        Assert.DoesNotContain("kaydettim", response.Reply);
        Assert.True(response.GuardrailTriggered); // success=false semantiği DTO'da korunur
    }

    [Fact]
    public async Task Hardening3_SuccessfulPersistence_ProducesDeterministicSuccessReply()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueToolCall("call_save", "WorkoutPlan-SaveWorkoutPlan", PushPlanJson);

        var response = await h.ChatAsync(UserA, "Bana bir push antrenmanı oluştur ve kaydet.");

        Assert.Equal("\"Push\" antrenman planın başarıyla kaydedildi (2 egzersiz, 3 set).", response.Reply);
        Assert.False(response.GuardrailTriggered);
        Assert.Empty(response.InterceptedProgressions);
        Assert.Single(h.Handler.Requests);

        // DB: 1 workout grafı, yalnızca current user (A) adına; modelin userId denemesi etkisiz.
        Assert.Equal((1, 2, 3), await h.NewRowsAsync());
        Assert.Equal(1, await h.NewWorkoutsForAsync(UserA));
        Assert.Equal(0, await h.NewWorkoutsForAsync(UserB));
    }

    [Fact]
    public async Task Hardening4_GuardrailRejection_DbUnchanged_DeterministicRejection_NoFalseSuccess()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueToolCall("call_save", "WorkoutPlan-SaveWorkoutPlan", UnsafeLegsPlanJson);
        h.Handler.EnqueueReply("Squat planın 150 kg ile kaydedildi."); // yanlış başarı beyanı hazır

        var response = await h.ChatAsync(UserA, "Bana bacak antrenmanı oluştur ve kaydet.");

        Assert.Equal("Plan kaydedilmedi. Squat için önerilen ağırlık güvenli ilerleme sınırını (132.0 kg) aşıyor.", response.Reply);
        Assert.True(response.GuardrailTriggered);
        Assert.Equal(new[] { "Squat: 150.0 kg > 132.0 kg (ACSM ≤10% rule, plan not saved)" }, response.InterceptedProgressions.ToArray());
        Assert.Equal((0, 0, 0), await h.NewRowsAsync());
        Assert.Single(h.Handler.Requests);
    }

    [Fact]
    public async Task Hardening_DeterministicReplies_AreLocalized_English()
    {
        using (var ok = Harness.Create())
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            ok.Handler.EnqueueToolCall("c", "WorkoutPlan-SaveWorkoutPlan", PushPlanJson);
            var saved = await ok.ChatAsync(UserA, "Create a push workout and save it.");
            Assert.Equal("Your \"Push\" workout plan was saved successfully (2 exercises, 3 sets).", saved.Reply);
        }

        using (var bad = Harness.Create())
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            bad.Handler.EnqueueToolCall("c", "WorkoutPlan-SaveWorkoutPlan", UnsafeLegsPlanJson);
            var rejected = await bad.ChatAsync(UserA, "Create a leg workout and save it.");
            Assert.Equal("The plan was not saved. The weight suggested for Squat exceeds the safe progression limit (132.0 kg).", rejected.Reply);
        }
    }

    [Fact]
    public async Task Hardening_InvalidPlan_GetsDeterministicReply_WithoutSecondLlmCall()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueToolCall("c", "WorkoutPlan-SaveWorkoutPlan", """{"workoutName":"Push","exercises":[]}""");
        h.Handler.EnqueueReply("Kaydettim.");

        var response = await h.ChatAsync(UserA, "kaydet");

        Assert.Equal("Antrenman planındaki bilgiler geçersiz olduğu için kaydedilmedi.", response.Reply);
        Assert.Single(h.Handler.Requests);
        Assert.Equal((0, 0, 0), await h.NewRowsAsync());
    }

    [Fact]
    public async Task Hardening_SetNumbers_AreAssignedByServer_ModelSuppliedNumbersAreIgnored_StringRepsAccepted()
    {
        using var h = Harness.Create();
        // Eski sözleşmedeki gibi (bozuk) setNumber'lar ve string reps: setNumber güvenilen bir kaynak değil.
        h.Handler.EnqueueToolCall("c", "WorkoutPlan-SaveWorkoutPlan", """
            {"workoutName":"Push","exercises":[{"exerciseName":"Bench Press","sets":[
              {"setNumber":7,"reps":"8","weightInKg":100},{"setNumber":7,"reps":"6","weightInKg":102.5},{"setNumber":0,"reps":5,"weightInKg":105}]}]}
            """);

        var response = await h.ChatAsync(UserA, "kaydet");

        Assert.StartsWith("\"Push\" antrenman planın başarıyla kaydedildi", response.Reply);
        var saved = await h.LoadWorkoutAsync(await h.LatestNewWorkoutIdAsync());
        var sets = saved.Exercises.Single().ExerciseSets.OrderBy(s => s.SetNumber).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, sets.Select(s => s.SetNumber).ToArray());          // sunucu: index + 1
        Assert.Equal(new[] { "8", "6", "5" }, sets.Select(s => s.Reps).ToArray());            // modelin sırası korunur
        Assert.Equal(new[] { 100, 102.5, 105 }, sets.Select(s => s.WeightInKg).ToArray());
    }

    [Fact]
    public async Task Hardening_TwoSaveCallsInOneModelResponse_OnlyOnePlanIsPersisted_SingleLlmCall()
    {
        using var h = Harness.Create();
        const string plan = """{"workoutName":"Push","exercises":[{"exerciseName":"Bench Press","sets":[{"reps":8,"weightInKg":105}]}]}""";
        h.Handler.EnqueueToolCalls(("c1", "WorkoutPlan-SaveWorkoutPlan", plan), ("c2", "WorkoutPlan-SaveWorkoutPlan", plan));
        h.Handler.EnqueueReply("Kaydedildi.");

        var response = await h.ChatAsync(UserA, "kaydet");

        Assert.Equal((1, 1, 1), await h.NewRowsAsync());
        Assert.Single(h.Handler.Requests);
        Assert.StartsWith("\"Push\" antrenman planın başarıyla kaydedildi", response.Reply);
    }

    [Fact]
    public async Task Hardening_ReadTools_KeepTheirLlmToolLlmFlow_WhenWritePluginIsRegistered()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueToolCall("r1", "Workout-GetWeightTrends", "{}");
        h.Handler.EnqueueReply("Bench Press'te 95 kg'dan 100 kg'a çıktın.");

        var response = await h.ChatAsync(UserA, "Bench trendim?");

        Assert.Equal(2, h.Handler.Requests.Count); // okuma tool'u: sonuç LLM'e geri döner
        Assert.Equal("Bench Press'te 95 kg'dan 100 kg'a çıktın.", response.Reply);
        Assert.Equal((0, 0, 0), await h.NewRowsAsync());
    }

    [Fact]
    public async Task Orchestration_ToolIsOfferedOnlyInFreeChat_WithCompactSchema()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueReply("ok");
        h.Handler.EnqueueReply("ok");

        await h.ChatAsync(UserA, "x", "free");
        await h.ChatAsync(UserA, "x", "program"); // preset: tool yok (mevcut davranış)

        var free = Json(h.Handler.Requests[0]).GetProperty("tools").EnumerateArray()
            .ToDictionary(t => t.GetProperty("function").GetProperty("name").GetString()!, t => t.GetRawText());
        Assert.Contains("WorkoutPlan-SaveWorkoutPlan", free.Keys);
        Assert.Equal(4, free.Count); // 3 okuma + 1 yazma

        var schema = free["WorkoutPlan-SaveWorkoutPlan"];
        // Şemadaki hiçbir alan adı kimlik/gizli bilgi taşımaz (açıklama metnindeki "current user" ifadesi hariç).
        var propertyNames = new List<string>();
        void Collect(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                {
                    if (p.NameEquals("properties") && p.Value.ValueKind == JsonValueKind.Object)
                        propertyNames.AddRange(p.Value.EnumerateObject().Select(x => x.Name));
                    Collect(p.Value);
                }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var item in e.EnumerateArray()) Collect(item);
        }
        using (var schemaDoc = JsonDocument.Parse(schema)) Collect(schemaDoc.RootElement);
        Assert.Equal(
            new[] { "exerciseName", "exercises", "reps", "sets", "weightInKg", "workoutDate", "workoutName" }, // setNumber yok
            propertyNames.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.True(schema.Length < 1500, $"SaveWorkoutPlan tool schema is {schema.Length} chars; keep it compact (token budget).");

        Assert.False(Json(h.Handler.Requests[1]).TryGetProperty("tools", out _));
    }

    // ───────────────────── Helpers ─────────────────────

    // Model sözleşmesindeki gibi: set numarası YOK, sunucu 1..N atar.
    private static WorkoutPlanExerciseDto Ex(string name, params double[] weights) => new()
    {
        ExerciseName = name,
        Sets = weights.Select(w => new WorkoutPlanSetDto { Reps = 8, WeightInKg = w }).ToList()
    };

    private static JsonElement Json(CapturedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Body);
        return doc.RootElement.Clone();
    }

    private sealed record CapturedRequest(string Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();
        public List<CapturedRequest> Requests { get; } = new();
        public int PendingResponses => _responses.Count;

        public void EnqueueReply(string content) => _responses.Enqueue(Completion(new { role = "assistant", content }, "stop"));

        public void EnqueueToolCall(string id, string name, string argumentsJson) => EnqueueToolCalls((id, name, argumentsJson));

        public void EnqueueToolCalls(params (string Id, string Name, string ArgumentsJson)[] calls) => _responses.Enqueue(Completion(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = calls.Select(c => new { id = c.Id, type = "function", function = new { name = c.Name, arguments = c.ArgumentsJson } }).ToArray()
        }, "tool_calls"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (_responses.Count == 0)
                throw new InvalidOperationException("Test beklenmeyen bir Groq çağrısı yaptı (kuyrukta cevap yok).");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }

        private static string Completion(object message, string finishReason) => JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            @object = "chat.completion",
            created = 1700000000,
            model = "openai/gpt-oss-120b",
            choices = new[] { new { index = 0, message, logprobs = (object?)null, finish_reason = finishReason } },
            usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 }
        });
    }

    /// <summary>Belirli bir tabloya INSERT sırasında hata fırlatır (DB katmanında kısmi yazım/rollback testi).</summary>
    private sealed class FailingInsertInterceptor : DbCommandInterceptor
    {
        public string? FailOnInsertInto { get; set; }
        public List<string> ExecutedInserts { get; } = new();

        private void Inspect(DbCommand command)
        {
            const string marker = "INSERT INTO \"";
            var text = command.CommandText;
            var start = text.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return;

            var table = text.Substring(start + marker.Length, text.IndexOf('"', start + marker.Length) - start - marker.Length);
            if (table == FailOnInsertInto)
                throw new InvalidOperationException("SECRET simulated SQL failure: Server=prod;Password=hunter2");

            ExecutedInserts.Add(table);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Inspect(command); return base.ReaderExecuting(command, eventData, result); }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Inspect(command); return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        { Inspect(command); return base.NonQueryExecuting(command, eventData, result); }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Inspect(command); return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken); }
    }

    private sealed class Harness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly (int Workouts, int Exercises, int Sets) _seeded;

        public ServiceProvider Provider { get; }
        public StubHandler Handler { get; } = new();
        public FailingInsertInterceptor Interceptor { get; } = new();

        private Harness()
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            // SQLite in-memory: gerçek transaction/rollback semantiği (EF InMemory provider bunu modellemez).
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = "test-key",
                ["Groq:Model"] = "openai/gpt-oss-120b"
            }).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddDbContext<FitTrackrDbContext>(o => o.UseSqlite(_connection).AddInterceptors(Interceptor));
            services.AddScoped<IWorkoutRepository, WorkoutRepository>();          // Program.cs ile aynı
            services.AddScoped<IWorkoutAnalysisService, WorkoutAnalysisService>();
            services.AddSingleton<IAcsmGuardrailService, AcsmGuardrailService>();
            services.AddValidatorsFromAssemblyContaining<WorkoutRequestDtoValidator>();
            services.AddGroqSemanticKernel();
            services.AddFitBotWorkoutPlugin();
            services.AddFitBotWorkoutPlanPlugin();
            services.AddScoped<IAiWorkoutCoachService, AiWorkoutCoachService>();
            services.AddHttpClient(GroqChatCompletion.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Handler);

            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            db.Database.EnsureCreated(); // şema + Intensity/Location seed verisi
            var medium = db.Intensities.Single(i => i.Level == "Medium").Id;

            Seed(db, medium, UserA, daysAgo: 10, ("Bench Press", 95));
            Seed(db, medium, UserA, daysAgo: 3, ("Bench Press", 100), ("Squat", 120));
            Seed(db, medium, UserB, daysAgo: 3, ("Bench Press", 200), ("Squat", 250));
            db.SaveChanges();

            _seeded = (db.Workouts.Count(), db.Exercises.Count(), db.ExerciseSets.Count());
            Interceptor.ExecutedInserts.Clear();
        }

        public static Harness Create() => new();

        private static void Seed(FitTrackrDbContext db, Guid intensityId, string userId, int daysAgo, params (string Name, double Kg)[] exercises) =>
            db.Workouts.Add(new Workout
            {
                WorkoutName = "Seed",
                WorkoutDate = DateTime.UtcNow.Date.AddDays(-daysAgo),
                userId = userId,
                Exercises = exercises.Select(e => new Exercise
                {
                    ExerciseName = e.Name,
                    IntensityId = intensityId,
                    ExerciseSets = new() { new ExerciseSet { SetNumber = 1, Reps = "5", WeightInKg = e.Kg } }
                }).ToList()
            });

        /// <summary>Kendi request scope'unda (production'daki gibi) SaveWorkoutPlan çalıştırır.</summary>
        public async Task<SaveWorkoutPlanResult> SaveAsync(string? userId, string? workoutName, params WorkoutPlanExerciseDto[] exercises) =>
            await SaveAsync(userId, workoutName, exercises.ToList(), null);

        public async Task<SaveWorkoutPlanResult> SaveAsync(string? userId, string? workoutName, List<WorkoutPlanExerciseDto>? exercises, string? workoutDate)
        {
            using var scope = Provider.CreateScope();
            if (userId is not null)
                scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(userId);
            return await InvokeSaveAsync(scope, workoutName, exercises, workoutDate);
        }

        public static async Task<SaveWorkoutPlanResult> InvokeSaveAsync(IServiceScope scope, string? workoutName, List<WorkoutPlanExerciseDto>? exercises, string? workoutDate = null)
        {
            var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();
            var args = new KernelArguments { ["workoutName"] = workoutName, ["exercises"] = exercises };
            if (workoutDate is not null)
                args["workoutDate"] = workoutDate;

            var result = await kernel.InvokeAsync(WorkoutPlanPlugin.PluginName, "SaveWorkoutPlan", args);
            return result.GetValue<SaveWorkoutPlanResult>()!;
        }

        public async Task<FitBotChatResponseDto> ChatAsync(string userId, string message, string actionType = "free")
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IAiWorkoutCoachService>()
                .ChatAsync(userId, new FitBotChatRequestDto { Message = message, ActionType = actionType });
        }

        public async Task<FitBotContextDto> ContextForAsync(string userId)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IWorkoutAnalysisService>().GetFitBotContextAsync(userId);
        }

        /// <summary>Seed'den sonra eklenen satır sayıları (ayrı, temiz bir DbContext ile).</summary>
        public async Task<(int Workouts, int Exercises, int Sets)> NewRowsAsync()
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            return (await db.Workouts.CountAsync() - _seeded.Workouts,
                    await db.Exercises.CountAsync() - _seeded.Exercises,
                    await db.ExerciseSets.CountAsync() - _seeded.Sets);
        }

        public async Task<int> NewWorkoutsForAsync(string userId)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            return await db.Workouts.CountAsync(w => w.userId == userId && w.WorkoutName != "Seed");
        }

        public async Task<Guid> LatestNewWorkoutIdAsync()
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            return (await db.Workouts.AsNoTracking().Where(w => w.WorkoutName != "Seed").SingleAsync()).Id;
        }

        public async Task<Workout> LoadWorkoutAsync(Guid id)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            return await db.Workouts.AsNoTracking()
                .Include(w => w.Exercises).ThenInclude(e => e.ExerciseSets)
                .Include(w => w.Exercises).ThenInclude(e => e.Intensity)
                .SingleAsync(w => w.Id == id);
        }

        public void Dispose()
        {
            Provider.Dispose();
            _connection.Dispose();
        }
    }
}
