namespace FitTrackr.API.Models.Domain
{
    public class ExerciseSet
    {
        public Guid Id { get; set; }

        public int SetNumber { get; set; }

        public string Reps { get; set; }

        public double WeightInKg { get; set; }

        public Guid ExerciseId { get; set; }

        public Exercise Exercise { get; set; }
    }
}
