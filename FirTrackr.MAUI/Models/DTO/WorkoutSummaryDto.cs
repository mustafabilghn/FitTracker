using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitTrackr.MAUI.Models.DTO
{
    public class WorkoutSummaryDto
    {
        public Guid Id { get; set; }

        public string WorkoutName { get; set; }//upper,lower,push,pull,legs...

        public DayOfWeek WorkoutDate { get; set; }

        public double DurationMinutes { get; set; }

        public LocationDto Location { get; set; }
    }
}
