using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FitTrackr.UI.Models.DTO
{
    public class WorkoutRequestDto
    {
        [Required]
        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        [Required]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DayOfWeek WorkoutDate { get; set; }

        [Required]
        public double DurationMinutes { get; set; }

        [Required]
        public Guid LocationId { get; set; }
    }
}
