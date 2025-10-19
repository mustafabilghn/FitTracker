using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FitTrackr.MAUI.Validations
{
    public class ExerciseValidator
    {
        public static string Validate(string exerciseName,int sets,string reps,double weight,Guid intensityId)
        {
            if (string.IsNullOrWhiteSpace(exerciseName))
                return "Egzersiz adı boş geçilemez";

            if (sets <= 0)
                return "Set sayısı 0'dan büyük bir sayı olmalıdır";

            if (string.IsNullOrWhiteSpace(reps))
                return "Tekrar sayısı boş geçilemez";

            if (weight <= 0)
                return "Ağırlık 0'dan büyük bir sayı olmalıdır";

            if (intensityId == Guid.Empty)
                return "Egzersiz yoğunluğu seçilmelidir";

            return null;
        }
    }
}
