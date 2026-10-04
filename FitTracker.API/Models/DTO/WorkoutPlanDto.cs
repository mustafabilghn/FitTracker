using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FitTrackr.API.Models.DTO
{
    // FitBot'un SaveWorkoutPlan tool'unun yapılandırılmış girdisi. Şema her istekte token olarak gider; bu yüzden
    // alanlar ve açıklamalar bilinçli olarak kompakt tutulur. Kimlik/kullanıcı alanı YOKTUR (server-side belirlenir).
    // Set numarası da modelden ALINMAZ: sunucu, setlerin sırasına göre 1..N numaralar.

    public sealed class WorkoutPlanExerciseDto
    {
        [JsonPropertyName("exerciseName")]
        public string ExerciseName { get; set; } = string.Empty;

        [JsonPropertyName("sets")]
        public List<WorkoutPlanSetDto>? Sets { get; set; }
    }

    public sealed class WorkoutPlanSetDto
    {
        // Modeller tekrar sayısını bazen "8" (string) olarak gönderir; ikisi de kabul edilir.
        [JsonPropertyName("reps")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Reps { get; set; }

        [JsonPropertyName("weightInKg")]
        public double WeightInKg { get; set; }
    }
}
