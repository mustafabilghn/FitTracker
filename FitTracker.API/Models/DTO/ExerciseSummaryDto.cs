using System.ComponentModel.DataAnnotations;

namespace FitTrackr.API.Models.DTO
{
    public class ExerciseSummaryDto
    {
        public Guid Id { get; set; }

        [Required]
        public string ExerciseName { get; set; }//bench press

        public string? Notes { get; set; }

        public IntensityDto? Intensity { get; set; }
    }
}
