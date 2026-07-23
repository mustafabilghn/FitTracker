using FitTrackr.MAUI.Localization;
using FitTrackr.MAUI.Models.DTO;
using System.Threading.Tasks;

namespace FitTrackr.MAUI.Validations
{
    public static class WorkoutValidator
    {
       public static string Validate(string workoutName,double duration,Guid locationId,DateTime workoutDate)
        {
            if (string.IsNullOrEmpty(workoutName))
            {
                return LocalizationResourceManager.Instance["Workout_NameEmptyError"];
            }

            if (workoutDate == default)
            {
                return LocalizationResourceManager.Instance["Workout_DateRequiredError"];
            }

            if (duration <= 0)
            {
                return LocalizationResourceManager.Instance["Workout_DurationInvalidError"];
            }

            if (locationId == Guid.Empty)
            {
                return LocalizationResourceManager.Instance["Workout_LocationRequiredError"];
            }

            return null;
        }
    }
}
