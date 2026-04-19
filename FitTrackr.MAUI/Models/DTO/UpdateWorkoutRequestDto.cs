using System.ComponentModel.DataAnnotations;

namespace FitTrackr.MAUI.Models.DTO
{
    public class UpdateWorkoutRequestDto
    {
        [Required]
        public string WorkoutName { get; set; } = string.Empty;

        [Required]
        public DateTime WorkoutDate { get; set; }
    }
}