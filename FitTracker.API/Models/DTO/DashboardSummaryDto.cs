namespace FitTrackr.API.Models.DTO
{
    public class DashboardSummaryDto
    {
        /// <summary>
        /// Egzersizi olan antrenman günlerinin tarihleri (UTC Date). Streak hesabı için kullanılır.
        /// </summary>
        public List<DateTime> ActiveDates { get; set; } = new();

        public double BenchPressMaxKg { get; set; }
        public double SquatMaxKg { get; set; }
        public double DeadliftMaxKg { get; set; }
        public double BarbellRowMaxKg { get; set; }
        public double OhpMaxKg { get; set; }
    }
}
