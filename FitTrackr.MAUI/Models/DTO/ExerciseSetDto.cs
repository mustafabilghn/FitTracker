using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitTrackr.MAUI.Models.DTO
{
    public class ExerciseSetDto
    {
        public Guid Id { get; set; }

        public int SetNumber { get; set; }

        public string Reps { get; set; }

        public double WeightInKg { get; set; }

        public string DisplayText => $"{Reps} × {WeightInKg} kg";
    }
}
