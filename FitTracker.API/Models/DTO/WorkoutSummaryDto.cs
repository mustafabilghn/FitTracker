namespace FitTrackr.API.Models.DTO
{
    public class WorkoutSummaryDto
    {
        public Guid Id { get; set; }

        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        public DayOfWeek WorkoutDate { get; set; }

        public double DurationMinutes { get; set; }

        public LocationDto Location { get; set; }
    }
}
