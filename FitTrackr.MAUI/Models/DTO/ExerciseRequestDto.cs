using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitTrackr.MAUI.Models.DTO
{
    public class ExerciseRequestDto
    {
        [Required]
        public string ExerciseName { get; set; }//bench press

        public int Sets { get; set; }

        [Required]
        public string Reps { get; set; }

        public double WeightInKg { get; set; }

        [Required]
        public Guid IntensityId { get; set; }

        [Required]
        public Guid WorkoutId { get; set; }
    }
}
