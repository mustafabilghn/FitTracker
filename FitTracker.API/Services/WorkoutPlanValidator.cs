using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FitTrackr.API.Models.DTO;
using FluentValidation;

namespace FitTrackr.API.Services
{
    /// <summary>
    /// LLM tarafından üretilen yapılandırılmış antrenman planını, veritabanına ulaşmadan ÖNCE doğrular.
    ///
    /// - Antrenman adı/tarihi: mevcut <see cref="Validations.WorkoutRequestDtoValidator"/> kuralları.
    /// - Egzersiz adı: mevcut <see cref="Validations.ExerciseRequestDtoValidator"/> içindeki ExerciseName kuralları.
    /// - Setler: bu sınıftaki plan kuralları (mevcut bir set validator'ı yok).
    /// - Güvenlik: ACSM ≤%10 kuralı EGZERSİZ BAŞINA, o egzersizin kendi baseline'ına göre (<see cref="AcsmProgressionRule"/>).
    ///   Chat guardrail'inin satır bazlı metin eşleştirmesi (bilinen Gap4 sınırlaması) burada kullanılmaz.
    ///
    /// Güvensiz değerler asla düzeltilmez/kısılmaz: plan reddedilir.
    /// </summary>
    public sealed class WorkoutPlanValidator
    {
        public const int MaxExercises = 12;
        public const int MaxSetsPerExercise = 10;
        public const int MinReps = 1;
        public const int MaxReps = 100;
        public const double MaxWeightKg = 500;
        public const int MaxDaysAhead = 30;
        public const int PastDaysTolerance = 1; // UTC / yerel saat farkı toleransı

        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        private readonly IValidator<WorkoutRequestDto> _workoutValidator;
        private readonly IValidator<ExerciseRequestDto> _exerciseValidator;

        public WorkoutPlanValidator(IValidator<WorkoutRequestDto> workoutValidator, IValidator<ExerciseRequestDto> exerciseValidator)
        {
            _workoutValidator = workoutValidator;
            _exerciseValidator = exerciseValidator;
        }

        public async Task<WorkoutPlanValidationResult> ValidateAsync(
            string? workoutName,
            string? workoutDate,
            IReadOnlyList<WorkoutPlanExerciseDto>? exercises,
            IReadOnlyDictionary<string, double> baselines,
            DateTime todayUtc,
            CancellationToken cancellationToken = default)
        {
            // ── Workout ──
            var name = Normalize(workoutName);
            if (!TryResolveDate(workoutDate, todayUtc, out var date, out var dateError))
                return WorkoutPlanValidationResult.Invalid(dateError);

            var workoutResult = await _workoutValidator.ValidateAsync(
                new WorkoutRequestDto { WorkoutName = name, WorkoutDate = date }, cancellationToken);
            if (!workoutResult.IsValid)
                return WorkoutPlanValidationResult.Invalid(
                    workoutResult.Errors.Any(e => e.PropertyName == nameof(WorkoutRequestDto.WorkoutName)) ? "invalid_workout_name" : "invalid_date");

            // ── Exercises ──
            if (exercises is null || exercises.Count == 0)
                return WorkoutPlanValidationResult.Invalid("exercises_required");
            if (exercises.Count > MaxExercises)
                return WorkoutPlanValidationResult.Invalid("too_many_exercises");

            var validated = new List<ValidatedPlanExercise>(exercises.Count);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var exercise in exercises)
            {
                if (exercise is null)
                    return WorkoutPlanValidationResult.Invalid("invalid_exercise");

                var exerciseName = Normalize(exercise.ExerciseName);
                var nameResult = await _exerciseValidator.ValidateAsync(
                    new ExerciseRequestDto { ExerciseName = exerciseName },
                    options => options.IncludeProperties(nameof(ExerciseRequestDto.ExerciseName)),
                    cancellationToken);
                if (!nameResult.IsValid)
                    return WorkoutPlanValidationResult.Invalid("invalid_exercise_name", exerciseName);

                if (!seenNames.Add(exerciseName))
                    return WorkoutPlanValidationResult.Invalid("duplicate_exercise", exerciseName);

                var setError = ValidateSets(exercise.Sets);
                if (setError is not null)
                    return WorkoutPlanValidationResult.Invalid(setError, exerciseName);

                validated.Add(new ValidatedPlanExercise(
                    exerciseName,
                    exercise.Sets!.OrderBy(s => s.SetNumber)
                        .Select(s => new ValidatedPlanSet(s.SetNumber, s.Reps, s.WeightInKg))
                        .ToList()));
            }

            // ── ACSM guardrail: egzersiz başına, kendi baseline'ı ile ──
            foreach (var exercise in validated)
            {
                if (!baselines.TryGetValue(exercise.Name, out var baselineKg) || baselineKg <= 0)
                    continue; // geçmiş yok → ilerleme kuralı uygulanamaz (chat guardrail ile aynı semantik)

                if (exercise.Sets.Any(s => !AcsmProgressionRule.IsWithinLimit(s.WeightInKg, baselineKg)))
                    return WorkoutPlanValidationResult.GuardrailViolation(
                        exercise.Name, Math.Round(AcsmProgressionRule.SafeMaxKg(baselineKg), 2));
            }

            return WorkoutPlanValidationResult.Valid(new ValidatedWorkoutPlan(name, date, validated));
        }

        private static string? ValidateSets(IReadOnlyList<WorkoutPlanSetDto>? sets)
        {
            if (sets is null || sets.Count == 0)
                return "sets_required";
            if (sets.Count > MaxSetsPerExercise)
                return "too_many_sets";
            if (sets.Any(s => s is null))
                return "invalid_set";

            // Mevcut uygulama konvansiyonu: set numaraları 1'den başlar ve ardışıktır (1..n). Tekrar/boşluk reddedilir.
            var numbers = sets.Select(s => s.SetNumber).OrderBy(n => n).ToList();
            if (!numbers.SequenceEqual(Enumerable.Range(1, sets.Count)))
                return "invalid_set_numbers";

            foreach (var set in sets)
            {
                if (set.Reps < MinReps || set.Reps > MaxReps)
                    return "invalid_reps";
                if (!IsValidWeight(set.WeightInKg))
                    return "invalid_weight";
            }

            return null;
        }

        private static bool IsValidWeight(double weightKg)
        {
            if (double.IsNaN(weightKg) || double.IsInfinity(weightKg) || weightKg < 0 || weightKg > MaxWeightKg)
                return false;

            // En fazla 0.01 kg çözünürlük: belirsiz/çok basamaklı değerler (110.0049 gibi) reddedilir, yuvarlanmaz.
            var asDecimal = (decimal)weightKg;
            return asDecimal == Math.Round(asDecimal, 2);
        }

        private static bool TryResolveDate(string? input, DateTime todayUtc, out DateTime date, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                date = todayUtc.Date;
                return true;
            }

            if (!DateTime.TryParseExact(input.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                error = "invalid_date";
                return false;
            }

            // Plan bir geçmiş kaydı uydurmamalı ve makul bir gelecek aralığında olmalı.
            if (date < todayUtc.Date.AddDays(-PastDaysTolerance) || date > todayUtc.Date.AddDays(MaxDaysAhead))
            {
                error = "date_out_of_range";
                return false;
            }

            return true;
        }

        // ExerciseRepository.SanitizeName ile aynı normalizasyon: trim + çoklu boşluk → tek boşluk.
        private static string Normalize(string? value) =>
            string.IsNullOrEmpty(value) ? string.Empty : Whitespace.Replace(value.Trim(), " ");
    }

    public sealed record ValidatedPlanSet(int SetNumber, int Reps, double WeightInKg);

    public sealed record ValidatedPlanExercise(string Name, IReadOnlyList<ValidatedPlanSet> Sets);

    public sealed record ValidatedWorkoutPlan(string WorkoutName, DateTime WorkoutDate, IReadOnlyList<ValidatedPlanExercise> Exercises);

    public sealed record WorkoutPlanValidationResult
    {
        public bool IsValid => Plan is not null;
        public ValidatedWorkoutPlan? Plan { get; private init; }
        public string? Reason { get; private init; }
        public string? Detail { get; private init; }
        public string? Exercise { get; private init; }
        public double? LimitKg { get; private init; }

        public static WorkoutPlanValidationResult Valid(ValidatedWorkoutPlan plan) => new() { Plan = plan };

        public static WorkoutPlanValidationResult Invalid(string detail, string? exercise = null) =>
            new() { Reason = "invalid_plan", Detail = detail, Exercise = string.IsNullOrEmpty(exercise) ? null : exercise };

        public static WorkoutPlanValidationResult GuardrailViolation(string exercise, double limitKg) =>
            new() { Reason = "guardrail_violation", Exercise = exercise, LimitKg = limitKg };
    }
}
