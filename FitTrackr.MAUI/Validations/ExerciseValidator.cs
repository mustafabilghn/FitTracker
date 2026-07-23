using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FitTrackr.MAUI.Localization;

namespace FitTrackr.MAUI.Validations
{
    public class ExerciseValidator
    {
        public static string Validate(string exerciseName, Guid intensityId)
        {
            if (string.IsNullOrWhiteSpace(exerciseName))
                return LocalizationResourceManager.Instance["Exercise_NameEmptyError"];

            if (intensityId == Guid.Empty)
                return LocalizationResourceManager.Instance["Exercise_IntensityRequiredError"];

            return null;
        }
    }
}
