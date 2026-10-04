using FitTrackr.API.Models.Domain;

namespace FitTrackr.API.Models.DTO
{
    public class WorkoutDto
    {
        public Guid Id { get; set; }

        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        public DateTime WorkoutDate { get; set; }

        public WorkoutStatus Status { get; set; } // JSON: "Completed" | "Planned"

        public List<ExerciseSummaryDto> Exercises { get; set; }
    }
}
