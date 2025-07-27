using System.ComponentModel.DataAnnotations;

namespace FitTrackr.API.Models.Domain
{
    public class Workout
    {
        public Guid Id { get; set; }

        [Required]
        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        public DayOfWeek WorkoutDate { get; set; }

        public double DurationMinutes { get; set; }

        public Guid LocationId { get; set; }

        public Location Location { get; set; }

        public List<Exercise> Exercises { get; set; }
    }
}
