using System.ComponentModel.DataAnnotations;

namespace FitTrackr.UI.Models.DTO
{
    public class WorkoutRequestDto
    {
        [Required]
        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        [Required]
        [DataType(DataType.Date)]
        public DateTime WorkoutDate { get; set; }

        [Required]
        public double DurationMinutes { get; set; }

        [Required]
        public Guid LocationId { get; set; }
    }
}
