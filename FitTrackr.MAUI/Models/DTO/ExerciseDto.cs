using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace FitTrackr.MAUI.Models.DTO
{
    public class ExerciseDto : INotifyPropertyChanged
    {
        public Guid Id { get; set; }

        [Required]
        public string ExerciseName { get; set; }//bench press

        public string? Notes { get; set; }

        public IntensityDto? Intensity { get; set; }

        public List<ExerciseSetDto> ExerciseSets { get; set; }

        private bool isExpanded;

        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (isExpanded != value)
                {
                    isExpanded = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
