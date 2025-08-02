using System.ComponentModel.DataAnnotations;

namespace FitTrackr.UI.Models
{
    public class AddExerciseViewModel
    {
        [Required]
        public string ExerciseName { get; set; }//bench press

        public int Sets { get; set; }

        [Required]
        public string Reps { get; set; }

        public double WeightInKg { get; set; }

        [Required]
        public Guid IntensityId { get; set; }

        [Required]
        public Guid WorkoutId { get; set; }
    }
}
