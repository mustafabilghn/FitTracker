using System;
using System.Collections.Generic;

namespace FitTrackr.API.Models.DTO
{
    public class FitBotContextDto
    {
        public int TotalWorkouts { get; set; }
        public int DaysSinceLastWorkout { get; set; } = -1;
        public int WorkoutsThisWeek { get; set; }
        public int WorkoutsLast30Days { get; set; }
        public List<WorkoutContextEntryDto> RecentWorkouts { get; set; } = new();
        public List<ExerciseWeightTrendDto> WeightTrends { get; set; } = new();
        public List<string> PlateauExercises { get; set; } = new();
        public Dictionary<string, int> MuscleGroupFrequency { get; set; } = new();
    }

    public class WorkoutContextEntryDto
    {
        public string WorkoutName { get; set; } = string.Empty;
        public DateTime WorkoutDate { get; set; }
        public List<ExerciseContextEntryDto> Exercises { get; set; } = new();
    }

    public class ExerciseContextEntryDto
    {
        public string ExerciseName { get; set; } = string.Empty;
        public double MaxWeightKg { get; set; }
        public int SetCount { get; set; }
        public string Reps { get; set; } = string.Empty;
    }

    public class ExerciseWeightTrendDto
    {
        public string ExerciseName { get; set; } = string.Empty;
        public List<WeeklyMaxWeightDto> WeeklyMaxWeights { get; set; } = new();
        public string Trend { get; set; } = string.Empty;
    }

    public class WeeklyMaxWeightDto
    {
        public int WeeksAgo { get; set; }
        public double MaxKg { get; set; }
    }
}
