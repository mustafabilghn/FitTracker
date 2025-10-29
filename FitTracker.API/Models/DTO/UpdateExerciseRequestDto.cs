using System.ComponentModel.DataAnnotations;

namespace FitTrackr.API.Models.DTO
{
    public class UpdateExerciseRequestDto
    {
        [Required]
        public string ExerciseName { get; set; }//bench press

        public string? Notes { get; set; }

        [Required]
        public Guid IntensityId { get; set; }

        [Required]
        public Guid WorkoutId { get; set; }
    }
}
