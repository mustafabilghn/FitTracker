using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FitTrackr.API.Data;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Repositories;
using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace FitTrackr.API.Plugins
{
    /// <summary>
    /// FitBot'un tek WRITE aksiyonu: yapılandırılmış bir antrenman planını kimliği doğrulanmış kullanıcı adına kaydeder.
    ///
    /// Akış: current user (server-side) → mükerrer kayıt kontrolü → kullanıcının kendi baseline'ları
    /// (<see cref="IWorkoutAnalysisService.GetFitBotContextAsync"/>) → <see cref="WorkoutPlanValidator"/> (yapı + egzersiz
    /// başına ACSM ≤%10) → <see cref="IWorkoutRepository.CreateWithExercisesAsync"/> (atomik) → kompakt sonuç.
    ///
    /// Write lifecycle: Sonuç request-scoped <see cref="WorkoutPlanSaveOutcome"/>'a yazılır ve
    /// <see cref="WriteActionTerminationFilter"/> otomatik function-calling döngüsünü burada bitirir. Kullanıcıya dönen
    /// cevabı LLM DEĞİL, sunucu bu sonuçtan deterministik olarak üretir (bkz. AiWorkoutCoachService). Böylece başarılı
    /// bir kayıttan sonra ikinci bir LLM çağrısı (ve 429 riski) olmaz; model de başarısız bir kaydı "kaydedildi" diye
    /// raporlayamaz.
    ///
    /// Güvenlik: Fonksiyon <c>userId</c> veya başka bir kimlik argümanı ALMAZ; kayıt yalnızca <see cref="ICurrentUserContext"/>
    /// kullanıcısı adına yapılır. Kullanıcı yoksa hiçbir şey okunmaz/yazılmaz (fail closed).
    /// Güvensiz plan asla kısılarak/düzeltilerek kaydedilmez; tamamı reddedilir.
    ///
    /// Yaşam döngüsü: Scoped. Bir request scope'unda en fazla BİR başarılı kayıt yapılır. Reddedilen bir denemeden
    /// sonra düzeltilmiş planla tekrar denenebilir.
    /// </summary>
    public sealed class WorkoutPlanPlugin
    {
        public const string PluginName = "WorkoutPlan";
        public const string SaveFunctionName = nameof(SaveWorkoutPlan);

        // AI planı yoğunluk bilgisi içermez; seed edilmiş Medium/Orta seviyesi nötr varsayılandır.
        // Level metni yerelleştirildiği için stable seed ID'si kullanılır (tek kaynak: IntensitySeedIds).
        public static readonly Guid DefaultIntensityId = IntensitySeedIds.Medium;

        private const int Idle = 0;
        private const int Saving = 1;
        private const int Saved = 2;

        private readonly ICurrentUserContext _currentUser;
        private readonly IWorkoutAnalysisService _analysisService;
        private readonly IWorkoutRepository _workoutRepository;
        private readonly WorkoutPlanValidator _validator;
        private readonly WorkoutPlanSaveOutcome _outcome;
        private readonly ILogger<WorkoutPlanPlugin> _logger;
        private int _state = Idle;

        public WorkoutPlanPlugin(
            ICurrentUserContext currentUser,
            IWorkoutAnalysisService analysisService,
            IWorkoutRepository workoutRepository,
            WorkoutPlanValidator validator,
            WorkoutPlanSaveOutcome outcome,
            ILogger<WorkoutPlanPlugin> logger)
        {
            _currentUser = currentUser;
            _analysisService = analysisService;
            _workoutRepository = workoutRepository;
            _validator = validator;
            _outcome = outcome;
            _logger = logger;
        }

        [KernelFunction, Description("Saves a workout plan for the current user. Weights over 110% of the user's recent max per exercise are rejected.")]
        public async Task<SaveWorkoutPlanResult> SaveWorkoutPlan(
            [Description("Max 20 chars, e.g. 'Push'.")] string workoutName,
            [Description("Exercises in order; each set: reps 1-100, weightInKg >= 0.")] List<WorkoutPlanExerciseDto> exercises,
            [Description("yyyy-MM-dd. Default: today.")] string? workoutDate = null,
            CancellationToken cancellationToken = default)
        {
            var result = await SaveCoreAsync(workoutName, workoutDate, exercises, cancellationToken);
            _outcome.Record(result.Result, result.ExerciseCount, result.SetCount, result.RejectedWeightKg);
            return result.Result;
        }

        private async Task<(SaveWorkoutPlanResult Result, int ExerciseCount, int SetCount, double? RejectedWeightKg)> SaveCoreAsync(
            string workoutName, string? workoutDate, List<WorkoutPlanExerciseDto> exercises, CancellationToken cancellationToken)
        {
            var userId = _currentUser.UserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogWarning("SaveWorkoutPlan called without a current user.");
                return (SaveWorkoutPlanResult.Rejected(SaveWorkoutPlanReasons.NoCurrentUser), 0, 0, null);
            }

            // Aynı request scope'unda ikinci (veya eşzamanlı) kayıt denemesi reddedilir.
            var previous = Interlocked.CompareExchange(ref _state, Saving, Idle);
            if (previous != Idle)
                return (SaveWorkoutPlanResult.Rejected(SaveWorkoutPlanReasons.DuplicateSave), 0, 0, null);

            var saved = false;
            try
            {
                // Baseline yalnızca current user'ın geçmişinden (tek analiz kaynağı, kullanıcıya göre filtreli).
                var context = await _analysisService.GetFitBotContextAsync(userId);
                var baselines = AcsmProgressionRule.BuildBaselines(context);

                var validation = await _validator.ValidateAsync(
                    workoutName, workoutDate, exercises, baselines, DateTime.UtcNow.Date, cancellationToken);
                if (!validation.IsValid)
                    return (SaveWorkoutPlanResult.FromValidation(validation), 0, 0, validation.RejectedWeightKg);

                var plan = validation.Plan!;
                var created = await _workoutRepository.CreateWithExercisesAsync(
                    ToDomain(plan), userId, DefaultIntensityId, cancellationToken);
                if (created is null)
                {
                    _logger.LogError("SaveWorkoutPlan: intensity reference data {IntensityId} not found.", DefaultIntensityId);
                    return (SaveWorkoutPlanResult.Rejected(SaveWorkoutPlanReasons.SaveFailed), 0, 0, null);
                }

                saved = true;
                return (SaveWorkoutPlanResult.Succeeded(created.Id, created.WorkoutName),
                    plan.Exercises.Count, plan.Exercises.Sum(e => e.Sets.Count), null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // DB/altyapı detayı modele ve kullanıcıya sızdırılmaz.
                _logger.LogError(ex, "SaveWorkoutPlan failed.");
                return (SaveWorkoutPlanResult.Rejected(SaveWorkoutPlanReasons.SaveFailed), 0, 0, null);
            }
            finally
            {
                Volatile.Write(ref _state, saved ? Saved : Idle);
            }
        }

        [KernelFunction, Description("Current user's saved workout plans that are not done yet, soonest first.")]
        public async Task<object> GetPlannedWorkouts()
        {
            var userId = _currentUser.UserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogWarning("GetPlannedWorkouts called without a current user.");
                return new PlanToolError("The current user could not be identified.");
            }

            try
            {
                // Planned workout'lar yapılmış antrenman geçmişinden AYRI okunur (Completed history ile karışmaz).
                var plans = await _workoutRepository.GetPlannedAsync(userId);
                return plans
                    .Take(MaxPlannedWorkoutsReturned)
                    .Select(w => new PlannedWorkoutSummary(
                        w.Id,
                        w.WorkoutName,
                        w.WorkoutDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        (w.Exercises ?? new List<Exercise>()).Select(e => new PlannedExerciseSummary(
                            e.ExerciseName,
                            e.ExerciseSets?.Count ?? 0,
                            e.ExerciseSets is { Count: > 0 } sets ? sets.Max(s => s.WeightInKg) : 0)).ToList()))
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPlannedWorkouts failed.");
                return new PlanToolError("Workout plans are temporarily unavailable.");
            }
        }

        private const int MaxPlannedWorkoutsReturned = 10;

        private static Workout ToDomain(ValidatedWorkoutPlan plan) => new()
        {
            WorkoutName = plan.WorkoutName,
            WorkoutDate = plan.WorkoutDate,
            // FitBot planı henüz yapılmamış bir antrenmandır: analiz/trend/baseline'a girmez.
            Status = WorkoutStatus.Planned,
            Exercises = plan.Exercises.Select(e => new Exercise
            {
                ExerciseName = e.Name,
                ExerciseSets = e.Sets.Select(s => new ExerciseSet
                {
                    SetNumber = s.SetNumber, // sunucu tarafından atanmış (1..N)
                    Reps = s.Reps.ToString(CultureInfo.InvariantCulture), // domain: Reps string
                    WeightInKg = s.WeightInKg
                }).ToList()
            }).ToList()
        };
    }

    /// <summary>GetPlannedWorkouts için kompakt plan özeti (token bütçesi).</summary>
    public sealed record PlannedWorkoutSummary(
        [property: JsonPropertyName("workoutId")] Guid WorkoutId,
        [property: JsonPropertyName("workoutName")] string WorkoutName,
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("exercises")] List<PlannedExerciseSummary> Exercises);

    public sealed record PlannedExerciseSummary(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("sets")] int Sets,
        [property: JsonPropertyName("maxKg")] double MaxKg);

    internal sealed record PlanToolError([property: JsonPropertyName("error")] string Error);

    public static class SaveWorkoutPlanReasons
    {
        public const string NoCurrentUser = "no_current_user";
        public const string DuplicateSave = "duplicate_save";
        public const string InvalidPlan = "invalid_plan";
        public const string GuardrailViolation = "guardrail_violation";
        public const string SaveFailed = "save_failed";
    }

    /// <summary>Kompakt, makine-dostu tool sonucu. Altyapı detayı içermez.</summary>
    public sealed record SaveWorkoutPlanResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("workoutId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Guid? WorkoutId { get; init; }

        [JsonPropertyName("workoutName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? WorkoutName { get; init; }

        [JsonPropertyName("reason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Reason { get; init; }

        [JsonPropertyName("detail"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Detail { get; init; }

        [JsonPropertyName("exercise"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Exercise { get; init; }

        [JsonPropertyName("limitKg"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? LimitKg { get; init; }

        public static SaveWorkoutPlanResult Succeeded(Guid workoutId, string workoutName) =>
            new() { Success = true, WorkoutId = workoutId, WorkoutName = workoutName };

        public static SaveWorkoutPlanResult Rejected(string reason) => new() { Success = false, Reason = reason };

        public static SaveWorkoutPlanResult FromValidation(WorkoutPlanValidationResult validation) => new()
        {
            Success = false,
            Reason = validation.Reason,
            Detail = validation.Detail,
            Exercise = validation.Exercise,
            LimitKg = validation.LimitKg
        };
    }
}
