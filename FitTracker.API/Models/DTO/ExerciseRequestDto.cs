using System.ComponentModel.DataAnnotations;

namespace FitTrackr.API.Models.DTO
{
    public class ExerciseRequestDto
    {
        [Required]
        public string ExerciseName { get; set; }//bench press

        public int Sets { get; set; }   

        public string Reps { get; set; }

        public double WeightInKg { get; set; }

        [Required]
        public Guid IntensityId { get; set; }

        [Required]
        public Guid WorkoutId { get; set; }
    }
}
