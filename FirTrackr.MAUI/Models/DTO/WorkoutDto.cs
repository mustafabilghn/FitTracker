namespace FitTrackr.MAUI.Models.DTO
{
    public class WorkoutDto
    {
        public Guid Id { get; set; }

        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        public DayOfWeek WorkoutDate { get; set; }

        public double DurationMinutes { get; set; }

        public LocationDto Location { get; set; }

        public List<ExerciseDto> Exercises { get; set; }
    }
}
