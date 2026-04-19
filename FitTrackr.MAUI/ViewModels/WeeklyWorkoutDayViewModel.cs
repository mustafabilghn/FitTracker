using CommunityToolkit.Mvvm.ComponentModel;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class WeeklyWorkoutDayViewModel : ObservableObject
    {
        public DateTime Date { get; }

        public string DayLabel => Date.ToString("ddd");

        public string DayNumber => Date.Day.ToString();

        [ObservableProperty]
        private bool isCompleted;

        [ObservableProperty]
        private bool isSelected;

        public WeeklyWorkoutDayViewModel(DateTime date)
        {
            Date = date.Date;
        }
    }
}
