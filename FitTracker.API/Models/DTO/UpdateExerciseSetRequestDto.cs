namespace FitTrackr.API.Models.DTO
{
    public class UpdateExerciseSetRequestDto
    {
        public int SetNumber { get; set; }

        public string Reps { get; set; }

        public double WeightInKg { get; set; }
    }
}
