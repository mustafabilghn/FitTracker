namespace FitTrackr.MAUI.Services
{
    /// <summary>
    /// Timezone-safe DateTime utilities for date-based operations.
    /// Backend always stores dates as UTC; this handles client-side local conversion.
    /// </summary>
    public static class DateTimeExtensions
    {
        /// <summary>
        /// Converts UTC DateTime to local date for display purposes.
        /// Safe across timezone boundaries and DST transitions.
        /// </summary>
        /// <param name="utcDateTime">DateTime from backend (UTC)</param>
        /// <returns>Local date portion only</returns>
        public static DateTime ToLocalDate(this DateTime utcDateTime)
        {
            if (utcDateTime.Kind != DateTimeKind.Utc)
            {
                utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
            }

            return utcDateTime.ToLocalTime().Date;
        }

        /// <summary>
        /// Converts local date to UTC start of day.
        /// Used for backend queries or date comparisons.
        /// </summary>
        /// <param name="localDate">Local date</param>
        /// <returns>DateTime set to midnight UTC for this local date</returns>
        public static DateTime ToUtcStart(this DateTime localDate)
        {
            var local = localDate.Date;
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.Local);
            return utc;
        }

        /// <summary>
        /// Safe date comparison accounting for timezone differences.
        /// Compares only the date portion after converting to local time.
        /// </summary>
        /// <param name="utcDateTime">DateTime from backend (UTC)</param>
        /// <param name="localDate">Local date to compare against</param>
        /// <returns>True if dates match after timezone conversion</returns>
        public static bool IsSameLocalDate(this DateTime utcDateTime, DateTime localDate)
        {
            return utcDateTime.ToLocalDate() == localDate.Date;
        }
    }
}
