using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitTrackr.MAUI.Validations
{
    public class ExerciseValidator
    {
        public static string Validate(string exerciseName, Guid intensityId)
        {
            if (string.IsNullOrWhiteSpace(exerciseName))
                return "Egzersiz adı boş geçilemez";

            if (intensityId == Guid.Empty)
                return "Egzersiz yoğunluğu seçilmelidir";

            return null;
        }
    }
}
