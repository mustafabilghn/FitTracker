using System.ComponentModel.DataAnnotations;

namespace FitTrackr.API.Models.Domain
{
    public class Exercise
    {
        public Guid Id { get; set; }

        [Required]
        public string ExerciseName { get; set; }//bench press

        public int Sets { get; set; }

        public string Reps { get; set; }

        public double? WeightInKg { get; set; }

        public Guid IntensityId { get; set; }

        public Guid WorkoutId { get; set; }

        public Intensity Intensity { get; set; }

        public Workout Workout { get; set; }

    }
}
