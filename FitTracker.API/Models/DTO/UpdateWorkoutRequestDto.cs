using System.ComponentModel.DataAnnotations;

namespace FitTrackr.API.Models.DTO
{
    public class UpdateWorkoutRequestDto
    {
        [Required]
        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        public DayOfWeek WorkoutDate { get; set; }

        public double DurationMinutes { get; set; }

        public Guid LocationId { get; set; }
    }
}
