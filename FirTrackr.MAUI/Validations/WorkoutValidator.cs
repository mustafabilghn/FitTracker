using FitTrackr.MAUI.Models.DTO;
using System.Threading.Tasks;

namespace FitTrackr.MAUI.Validations
{
    public static class WorkoutValidator
    {
       public static string Validate(string workoutName,double duration,Guid locationId,DayOfWeek? workoutDate)
        {
            if (string.IsNullOrEmpty(workoutName))
            {
                return "Antrenman adı boş olamaz.";
            }

            if (workoutDate == null)
            {
                return "Gün seçilmelidir.";
            }

            if (duration <= 0)
            {
                return "Süre 0'dan büyük bir sayı olmalıdır.";
            }

            if (locationId == Guid.Empty)
            {
                return "Yer seçilmelidir.";
            }

            return null;
        }
    }
}
