using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitTrackr.MAUI.Models.DTO
{
    public class ExerciseSetRequestDto
    {
        public int SetNumber { get; set; }

        public string Reps { get; set; }

        public double WeightInKg { get; set; }

        public Guid ExerciseId { get; set; }
    }
}
