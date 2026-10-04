using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
    /// Güvenlik: Fonksiyon <c>userId</c> veya başka bir kimlik argümanı ALMAZ; kayıt yalnızca <see cref="ICurrentUserContext"/>
    /// kullanıcısı adına yapılır. Kullanıcı yoksa hiçbir şey okunmaz/yazılmaz (fail closed).
    /// Güvensiz plan asla kısılarak/düzeltilerek kaydedilmez; tamamı reddedilir.
    ///
    /// Yaşam döngüsü: Scoped. Bir request scope'unda en fazla BİR başarılı kayıt yapılır (aynı agent çalışmasında
    /// modelin aynı planı tekrar kaydetmesini engeller). Reddedilen bir denemeden sonra düzeltilmiş planla tekrar denenebilir.
    /// </summary>
    public sealed class WorkoutPlanPlugin
    {
        public const string PluginName = "WorkoutPlan";

        // AI planı yoğunluk bilgisi içermez; seed edilmiş "Medium" seviyesi nötr varsayılandır.
        public const string DefaultIntensityLevel = "Medium";

        private const int Idle = 0;
        private const int Saving = 1;
        private const int Saved = 2;

        private readonly ICurrentUserContext _currentUser;
        private readonly IWorkoutAnalysisService _analysisService;
        private readonly IWorkoutRepository _workoutRepository;
        private readonly WorkoutPlanValidator _validator;
        private readonly ILogger<WorkoutPlanPlugin> _logger;
        private int _state = Idle;

        public WorkoutPlanPlugin(
            ICurrentUserContext currentUser,
            IWorkoutAnalysisService analysisService,
            IWorkoutRepository workoutRepository,
            WorkoutPlanValidator validator,
            ILogger<WorkoutPlanPlugin> logger)
        {
            _currentUser = currentUser;
            _analysisService = analysisService;
            _workoutRepository = workoutRepository;
            _validator = validator;
            _logger = logger;
        }

        [KernelFunction, Description("Saves a workout plan for the current user. Weights over 110% of the user's recent max per exercise are rejected, not saved.")]
        public async Task<SaveWorkoutPlanResult> SaveWorkoutPlan(
            [Description("Max 20 chars, e.g. 'Push'.")] string workoutName,
            [Description("Exercises; sets numbered 1..n, reps 1-100, weightInKg >= 0.")] List<WorkoutPlanExerciseDto> exercises,
            [Description("yyyy-MM-dd. Default: today.")] string? workoutDate = null,
            CancellationToken cancellationToken = default)
        {
            var userId = _currentUser.UserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogWarning("SaveWorkoutPlan called without a current user.");
                return SaveWorkoutPlanResult.Rejected("no_current_user");
            }

            // Aynı request scope'unda ikinci (veya eşzamanlı) kayıt denemesi reddedilir.
            var previous = Interlocked.CompareExchange(ref _state, Saving, Idle);
            if (previous != Idle)
                return SaveWorkoutPlanResult.Rejected("duplicate_save");

            var saved = false;
            try
            {
                // Baseline yalnızca current user'ın geçmişinden (tek analiz kaynağı, kullanıcıya göre filtreli).
                var context = await _analysisService.GetFitBotContextAsync(userId);
                var baselines = AcsmProgressionRule.BuildBaselines(context);

                var validation = await _validator.ValidateAsync(
                    workoutName, workoutDate, exercises, baselines, DateTime.UtcNow.Date, cancellationToken);
                if (!validation.IsValid)
                    return SaveWorkoutPlanResult.FromValidation(validation);

                var created = await _workoutRepository.CreateWithExercisesAsync(
                    ToDomain(validation.Plan!), userId, DefaultIntensityLevel, cancellationToken);
                if (created is null)
                {
                    _logger.LogError("SaveWorkoutPlan: intensity reference data '{Level}' not found.", DefaultIntensityLevel);
                    return SaveWorkoutPlanResult.Rejected("save_failed");
                }

                saved = true;
                return SaveWorkoutPlanResult.Succeeded(created.Id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // DB/altyapı detayı modele sızdırılmaz.
                _logger.LogError(ex, "SaveWorkoutPlan failed.");
                return SaveWorkoutPlanResult.Rejected("save_failed");
            }
            finally
            {
                Volatile.Write(ref _state, saved ? Saved : Idle);
            }
        }

        private static Workout ToDomain(ValidatedWorkoutPlan plan) => new()
        {
            WorkoutName = plan.WorkoutName,
            WorkoutDate = plan.WorkoutDate,
            Exercises = plan.Exercises.Select(e => new Exercise
            {
                ExerciseName = e.Name,
                ExerciseSets = e.Sets.Select(s => new ExerciseSet
                {
                    SetNumber = s.SetNumber,
                    Reps = s.Reps.ToString(CultureInfo.InvariantCulture), // domain: Reps string
                    WeightInKg = s.WeightInKg
                }).ToList()
            }).ToList()
        };
    }

    /// <summary>Kompakt, makine-dostu tool sonucu. Altyapı detayı içermez.</summary>
    public sealed record SaveWorkoutPlanResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("workoutId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Guid? WorkoutId { get; init; }

        [JsonPropertyName("reason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Reason { get; init; }

        [JsonPropertyName("detail"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Detail { get; init; }

        [JsonPropertyName("exercise"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Exercise { get; init; }

        [JsonPropertyName("limitKg"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? LimitKg { get; init; }

        public static SaveWorkoutPlanResult Succeeded(Guid workoutId) => new() { Success = true, WorkoutId = workoutId };

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
