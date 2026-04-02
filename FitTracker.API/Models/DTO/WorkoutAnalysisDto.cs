using System;
using System.Collections.Generic;

namespace FitTrackr.API.Models.DTO
{
    public class WorkoutAnalysisDto
    {
        public int TotalWorkouts { get; set; }

        public DateTime? LastWorkoutDate { get; set; }

        public int WorkoutsLast30Days { get; set; }

        public int TotalExercises { get; set; }

        public int TotalSets { get; set; }

        public List<string> MostFrequentExercises { get; set; } = new();

        public Dictionary<string, int> IntensityDistribution { get; set; } = new();
    }
}


