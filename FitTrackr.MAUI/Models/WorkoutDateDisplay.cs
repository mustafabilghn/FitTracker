using System.Globalization;
using FitTrackr.MAUI.Localization;

namespace FitTrackr.MAUI.Models
{
    /// <summary>
    /// Shared formatting for workout dates (follows the app's currently selected language,
    /// e.g. "01 Nis 2026 • Pazartesi" in Turkish or "01 Apr 2026 • Monday" in English).
    /// </summary>
    internal static class WorkoutDateDisplay
    {
        private static CultureInfo Culture => LocalizationResourceManager.Instance.CurrentCulture;

        /// <summary>Date plus weekday, e.g. "01 Nis 2026 • Pazartesi".</summary>
        public static string FormatDateAndWeekday(DateTime workoutDate)
        {
            var datePart = workoutDate.ToString("dd MMM yyyy", Culture);
            var dayPart = Culture.DateTimeFormat.GetDayName(workoutDate.DayOfWeek);
            return $"{datePart} • {dayPart}";
        }
    }
}
