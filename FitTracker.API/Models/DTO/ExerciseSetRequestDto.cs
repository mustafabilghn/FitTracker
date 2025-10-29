namespace FitTrackr.API.Models.DTO
{
    public class ExerciseSetRequestDto
    {
        public int SetNumber { get; set; }

        public string Reps { get; set; }

        public double WeightInKg { get; set; }

        public Guid ExerciseId { get; set; }
    }
}
