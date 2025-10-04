namespace FitTrackr.MAUI.Validations
{
    public class WorkoutRequestValidator
    {
        public static string Validate(string workoutName, string dayInput, string duration, object location)
        {
            if (string.IsNullOrWhiteSpace(workoutName))
            {
                return "Antrenman adı boş olamaz";
            }

            if (string.IsNullOrWhiteSpace(dayInput))
            {
                return "Gün boş bırakılamaz";
            }

            if (!double.TryParse(duration, out var dur) || dur <= 0)
            {
                return "Süre geçerli bir sayı olmalı ve 0'dan büyük olmalı";
            }

            if (location == null)
            {
                return "Yer boş bırakılamaz";
            }

            return null;
        }
    }
}
