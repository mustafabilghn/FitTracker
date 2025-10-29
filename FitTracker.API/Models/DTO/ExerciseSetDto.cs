namespace FitTrackr.API.Models.DTO
{
    public class ExerciseSetDto
    {
        public Guid Id { get; set; }

        public int SetNumber { get; set; }

        public string Reps { get; set; }

        public double WeightInKg { get; set; }
    }
}
