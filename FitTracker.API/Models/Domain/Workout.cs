using System.ComponentModel.DataAnnotations;

namespace FitTrackr.API.Models.Domain
{
    public class Workout
    {
        public Guid Id { get; set; }

        [Required]
        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        public DateTime WorkoutDate { get; set; }

        public List<Exercise> Exercises { get; set; }

        public string userId { get; set; }
    }
}
