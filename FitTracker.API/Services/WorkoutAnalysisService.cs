using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FitTrackr.API.Data;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

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
    }
}