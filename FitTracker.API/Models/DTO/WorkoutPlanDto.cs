using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FitTrackr.API.Models.DTO
{
    // FitBot'un SaveWorkoutPlan tool'unun yapılandırılmış girdisi. Şema her istekte token olarak gider; bu yüzden
    // alanlar ve açıklamalar bilinçli olarak kompakt tutulur. Kimlik/kullanıcı alanı YOKTUR (server-side belirlenir).

    public sealed class WorkoutPlanExerciseDto
    {
        [JsonPropertyName("exerciseName")]
        public string ExerciseName { get; set; } = string.Empty;

        [JsonPropertyName("sets")]
        public List<WorkoutPlanSetDto>? Sets { get; set; }
    }

    public sealed class WorkoutPlanSetDto
    {
        [JsonPropertyName("setNumber")]
        public int SetNumber { get; set; }

        [JsonPropertyName("reps")]
        public int Reps { get; set; }

        [JsonPropertyName("weightInKg")]
        public double WeightInKg { get; set; }
    }
}
