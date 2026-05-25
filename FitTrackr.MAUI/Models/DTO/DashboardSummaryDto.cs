namespace FitTrackr.MAUI.Models.DTO
{
    public class DashboardSummaryDto
    {
        public List<DateTime> ActiveDates { get; set; } = new();
        public double BenchPressMaxKg { get; set; }
        public double SquatMaxKg { get; set; }
        public double DeadliftMaxKg { get; set; }
        public double BarbellRowMaxKg { get; set; }
        public double OhpMaxKg { get; set; }
    }
}
