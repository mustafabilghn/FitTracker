using System.Globalization;

namespace FitTrackr.UI.Models.DTO
{
    public class WorkoutDto
    {
        public Guid Id { get; set; }

        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        public DateTime WorkoutDate { get; set; }

        public double DurationMinutes { get; set; }

        public Guid LocationId { get; set; }

        public LocationDto Location { get; set; }

        public List<ExerciseDto> Exercises { get; set; }

        public string WorkoutDateDisplay =>
            WorkoutDate.ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("tr-TR"))
            + " • "
            + CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetDayName(WorkoutDate.DayOfWeek);
    }
}
