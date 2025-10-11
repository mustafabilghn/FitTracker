using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace FitTrackr.MAUI.Models.DTO
{
    public class WorkoutRequestDto
    {
        [Required]
        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        [Required]
        public DayOfWeek WorkoutDate { get; set; }

        [Required]
        public double DurationMinutes { get; set; }

        [Required]
        public Guid LocationId { get; set; }

        public string WorkoutDateText => CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetDayName(WorkoutDate);
    }
}
