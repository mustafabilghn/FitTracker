using System.Globalization;

namespace FitTrackr.MAUI.Models
{
    /// <summary>
    /// Shared formatting for workout dates (matches existing tr-TR usage in the app).
    /// </summary>
    internal static class WorkoutDateDisplay
    {
        private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("tr-TR");

        /// <summary>Date plus weekday, e.g. "01 Nis 2026 • Pazartesi".</summary>
        public static string FormatDateAndWeekday(DateTime workoutDate)
        {
            var datePart = workoutDate.ToString("dd MMM yyyy", Culture);
            var dayPart = Culture.DateTimeFormat.GetDayName(workoutDate.DayOfWeek);
            return $"{datePart} • {dayPart}";
        }
    }
}
