using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FitTrackr.API.Data;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace FitTrackr.API.Services
{
    public class WorkoutAnalysisService : IWorkoutAnalysisService
    {
        private readonly FitTrackrDbContext _dbContext;

        public WorkoutAnalysisService(FitTrackrDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<WorkoutAnalysisDto> GetAnalysisAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new WorkoutAnalysisDto();
            }

            var workouts = await _dbContext.Workouts
                .AsNoTracking()
                .Where(w => w.userId == userId)
                .Include(w => w.Exercises)
                    .ThenInclude(e => e.ExerciseSets)
                .Include(w => w.Exercises)
                    .ThenInclude(e => e.Intensity)
                .ToListAsync();

            var dto = new WorkoutAnalysisDto
            {
                TotalWorkouts = workouts.Count
            };

            if (workouts.Count == 0)
            {
                return dto;
            }

            var cutoffDate = DateTime.UtcNow.Date.AddDays(-30);
            var exerciseFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var intensityDistribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int totalExercises = 0;
            int totalSets = 0;

            DateTime? lastWorkoutDate = null;

            foreach (var workout in workouts)
            {
                if (workout.WorkoutDate != default)
                {
                    lastWorkoutDate = lastWorkoutDate is null || workout.WorkoutDate > lastWorkoutDate
                        ? workout.WorkoutDate
                        : lastWorkoutDate;
                }

                if (workout.WorkoutDate.Date >= cutoffDate)
                {
                    dto.WorkoutsLast30Days++;
                }

                foreach (var exercise in workout.Exercises ?? Enumerable.Empty<Exercise>())
                {
                    if (exercise is null)
                    {
                        continue;
                    }

                    totalExercises++;

                    var exerciseName = exercise.ExerciseName?.Trim();
                    if (!string.IsNullOrWhiteSpace(exerciseName))
                    {
                        exerciseFrequency.TryGetValue(exerciseName, out var count);
                        exerciseFrequency[exerciseName] = count + 1;
                    }

                    var intensityLevel = exercise.Intensity?.Level?.Trim();
                    if (string.IsNullOrWhiteSpace(intensityLevel))
                    {
                        intensityLevel = "Unknown";
                    }

                    intensityDistribution.TryGetValue(intensityLevel, out var intensityCount);
                    intensityDistribution[intensityLevel] = intensityCount + 1;

                    foreach (var set in exercise.ExerciseSets ?? Enumerable.Empty<ExerciseSet>())
                    {
                        if (set is null)
                        {
                            continue;
                        }

                        totalSets++;
                    }
                }
            }

            dto.LastWorkoutDate = lastWorkoutDate;
            dto.TotalExercises = totalExercises;
            dto.TotalSets = totalSets;

            dto.MostFrequentExercises = exerciseFrequency
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Select(kv => kv.Key)
                .Take(3)
                .ToList();

            dto.IntensityDistribution = intensityDistribution;

            return dto;
        }

        public async Task<FitBotContextDto> GetFitBotContextAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return new FitBotContextDto();

            var today = DateTime.UtcNow.Date;
            var cutoff30 = today.AddDays(-30);
            var cutoff28 = today.AddDays(-28);

            var workouts = await _dbContext.Workouts
                .AsNoTracking()
                .Where(w => w.userId == userId)
                .Include(w => w.Exercises)
                    .ThenInclude(e => e.ExerciseSets)
                .OrderByDescending(w => w.WorkoutDate)
                .ToListAsync();

            var ctx = new FitBotContextDto { TotalWorkouts = workouts.Count };

            if (workouts.Count == 0)
                return ctx;

            ctx.DaysSinceLastWorkout = (int)(today - workouts[0].WorkoutDate.Date).TotalDays;
            ctx.WorkoutsThisWeek = workouts.Count(w => w.WorkoutDate.Date >= today.AddDays(-7));

            var workoutsLast30 = workouts.Where(w => w.WorkoutDate.Date >= cutoff30).ToList();
            ctx.WorkoutsLast30Days = workoutsLast30.Count;

            ctx.MuscleGroupFrequency = workoutsLast30
                .GroupBy(w => w.WorkoutName ?? "Unknown", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            ctx.RecentWorkouts = workoutsLast30
                .Take(10)
                .Select(w => new WorkoutContextEntryDto
                {
                    WorkoutName = w.WorkoutName ?? "Unknown",
                    WorkoutDate = w.WorkoutDate,
                    Exercises = (w.Exercises ?? Enumerable.Empty<Exercise>())
                        .Select(e => new ExerciseContextEntryDto
                        {
                            ExerciseName = e.ExerciseName ?? "Unknown",
                            MaxWeightKg = (e.ExerciseSets ?? Enumerable.Empty<ExerciseSet>())
                                .Select(s => s.WeightInKg).DefaultIfEmpty(0).Max(),
                            SetCount = (e.ExerciseSets ?? Enumerable.Empty<ExerciseSet>()).Count(),
                            Reps = (e.ExerciseSets ?? Enumerable.Empty<ExerciseSet>())
                                .FirstOrDefault()?.Reps ?? string.Empty
                        })
                        .ToList()
                })
                .ToList();

            // Weight trends: last 4 weeks per exercise
            var exerciseGroups = workouts
                .Where(w => w.WorkoutDate.Date >= cutoff28)
                .SelectMany(w => (w.Exercises ?? Enumerable.Empty<Exercise>())
                    .Select(e => new { Workout = w, Exercise = e }))
                .Where(x => x.Exercise != null && !string.IsNullOrWhiteSpace(x.Exercise.ExerciseName))
                .GroupBy(x => x.Exercise.ExerciseName.Trim(), StringComparer.OrdinalIgnoreCase);

            var trends = new List<ExerciseWeightTrendDto>();

            foreach (var group in exerciseGroups)
            {
                var weeklyMaxes = group
                    .GroupBy(x => (int)(today - x.Workout.WorkoutDate.Date).TotalDays / 7)
                    .Where(wg => wg.Key <= 3)
                    .Select(wg => new WeeklyMaxWeightDto
                    {
                        WeeksAgo = wg.Key,
                        MaxKg = wg.SelectMany(x => x.Exercise.ExerciseSets ?? Enumerable.Empty<ExerciseSet>())
                            .Select(s => s.WeightInKg)
                            .DefaultIfEmpty(0)
                            .Max()
                    })
                    .OrderBy(w => w.WeeksAgo)
                    .ToList();

                if (weeklyMaxes.Count < 2)
                    continue;

                trends.Add(new ExerciseWeightTrendDto
                {
                    ExerciseName = group.Key,
                    WeeklyMaxWeights = weeklyMaxes,
                    Trend = DetermineTrend(weeklyMaxes)
                });
            }

            // En fazla 15 egzersiz — veri büyüdüğünde prompt boyutunu sınırla
            ctx.WeightTrends = trends.OrderBy(t => t.ExerciseName).Take(15).ToList();

            ctx.PlateauExercises = trends
                .Where(t => IsOnPlateau(t.WeeklyMaxWeights))
                .Select(t => t.ExerciseName)
                .ToList();

            return ctx;
        }

        private static string DetermineTrend(List<WeeklyMaxWeightDto> weeklyMaxes)
        {
            // weeklyMaxes OrderBy(WeeksAgo) ile sıralı: First()=WeeksAgo=0=bu hafta (en güncel), Last()=en eski
            var mostRecent = weeklyMaxes.First().MaxKg;
            var oldestInRange = weeklyMaxes.Last().MaxKg;

            if (mostRecent > oldestInRange + 0.5) return "artıyor";
            if (oldestInRange > mostRecent + 0.5) return "düşüyor";
            return "sabit";
        }

        private static bool IsOnPlateau(List<WeeklyMaxWeightDto> weeklyMaxes)
        {
            var week0 = weeklyMaxes.FirstOrDefault(w => w.WeeksAgo == 0);
            var week1 = weeklyMaxes.FirstOrDefault(w => w.WeeksAgo == 1);
            var week2 = weeklyMaxes.FirstOrDefault(w => w.WeeksAgo == 2);

            if (week0 == null || week1 == null || week2 == null || week0.MaxKg == 0)
                return false;

            return Math.Abs(week0.MaxKg - week1.MaxKg) <= 0.5
                && Math.Abs(week1.MaxKg - week2.MaxKg) <= 0.5;
        }
    }
}