using System.ComponentModel.DataAnnotations;

namespace FitTrackr.MAUI.Models.DTO
{
    public class ExerciseDto
    {
        public Guid Id { get; set; }

        [Required]
        public string ExerciseName { get; set; }//bench press

        public int Sets { get; set; }

        public string Reps { get; set; }

        public double WeightInKg { get; set; }

        public IntensityDto? Intensity { get; set; }
    }
}
