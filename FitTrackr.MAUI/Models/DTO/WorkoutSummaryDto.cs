using FitTrackr.MAUI.Models;

namespace FitTrackr.MAUI.Models.DTO
{
    public class WorkoutSummaryDto
    {
        public Guid Id { get; set; }

        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        public DateTime WorkoutDate { get; set; }

        public double DurationMinutes { get; set; }

        public LocationDto Location { get; set; }

        public string WorkoutDateText => WorkoutDateDisplay.FormatDateAndWeekday(WorkoutDate);
    }
}
