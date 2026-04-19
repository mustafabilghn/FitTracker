namespace FitTrackr.MAUI.Models.DTO
{
    public class UpdateExerciseSetRequestDto
    {
        public int SetNumber { get; set; }

        public string Reps { get; set; } = string.Empty;

        public double WeightInKg { get; set; }
    }
}
