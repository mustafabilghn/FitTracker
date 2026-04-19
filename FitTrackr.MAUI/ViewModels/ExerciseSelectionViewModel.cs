using CommunityToolkit.Mvvm.ComponentModel;
using FitTrackr.MAUI.Models.DTO;
using System.Collections.ObjectModel;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class ExerciseSelectionViewModel : ObservableObject
    {
        private const string AllOption = "Tümü";

        private readonly List<ExerciseCatalogItemDto> allExercises;

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string selectedBodyPart = AllOption;

        [ObservableProperty]
        private string selectedEquipment = AllOption;

        [ObservableProperty]
        private string selectedLevel = AllOption;

        public string BodyPartDisplayText => SelectedBodyPart == AllOption ? "Bölge" : SelectedBodyPart;

        public string EquipmentDisplayText => SelectedEquipment == AllOption ? "Ekipman" : SelectedEquipment;

        public string LevelDisplayText => SelectedLevel == AllOption ? "Seviye" : SelectedLevel;

        public bool IsBodyPartFiltered => SelectedBodyPart != AllOption;

        public bool IsEquipmentFiltered => SelectedEquipment != AllOption;

        public bool IsLevelFiltered => SelectedLevel != AllOption;

        public ObservableCollection<string> BodyPartOptions { get; } = new();

        public ObservableCollection<string> EquipmentOptions { get; } = new();

        public ObservableCollection<string> LevelOptions { get; } = new();

        public ObservableCollection<ExerciseCatalogItemDto> FilteredExercises { get; } = new();

        public ExerciseSelectionViewModel()
        {
            allExercises =
            [
                new ExerciseCatalogItemDto { Id = Guid.NewGuid(), Name = "Bench Press", BodyPart = "Göğüs", Equipment = "Barbell", Level = "Orta", ImageName = "bench_press.png" },
                new ExerciseCatalogItemDto { Id = Guid.NewGuid(), Name = "Squat", BodyPart = "Bacak", Equipment = "Barbell", Level = "Orta" ,ImageName = "squat.png"},
                new ExerciseCatalogItemDto { Id = Guid.NewGuid(), Name = "Deadlift", BodyPart = "Sırt", Equipment = "Barbell", Level = "İleri" ,ImageName = "deadlift.png"},
                new ExerciseCatalogItemDto { Id = Guid.NewGuid(), Name = "Shoulder Press", BodyPart = "Omuz", Equipment = "Dumbbell", Level = "Başlangıç" ,ImageName = "dumbbell_shoulderpress.png"},
                new ExerciseCatalogItemDto { Id = Guid.NewGuid(), Name = "Lat Pulldown", BodyPart = "Sırt", Equipment = "Makine", Level = "Başlangıç" ,ImageName = "lat_pulldown.png"},
                new ExerciseCatalogItemDto { Id = Guid.NewGuid(), Name = "Leg Curl", BodyPart = "Arka Bacak", Equipment = "Makine", Level = "Başlangıç" ,ImageName = "leg_curl.png"}
            ];

            InitializeFilterOptions();
            ApplyFilter();
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedBodyPartChanged(string value)
        {
            OnPropertyChanged(nameof(BodyPartDisplayText));
            OnPropertyChanged(nameof(IsBodyPartFiltered));
            ApplyFilter();
        }

        partial void OnSelectedEquipmentChanged(string value)
        {
            OnPropertyChanged(nameof(EquipmentDisplayText));
            OnPropertyChanged(nameof(IsEquipmentFiltered));
            ApplyFilter();
        }

        partial void OnSelectedLevelChanged(string value)
        {
            OnPropertyChanged(nameof(LevelDisplayText));
            OnPropertyChanged(nameof(IsLevelFiltered));
            ApplyFilter();
        }

        private void InitializeFilterOptions()
        {
            PopulateOptions(BodyPartOptions, allExercises.Select(x => x.BodyPart));
            PopulateOptions(EquipmentOptions, allExercises.Select(x => x.Equipment));
            PopulateOptions(LevelOptions, allExercises.Select(x => x.Level));
        }

        private static void PopulateOptions(ObservableCollection<string> targetOptions, IEnumerable<string> sourceOptions)
        {
            targetOptions.Clear();
            targetOptions.Add(AllOption);

            foreach (var option in sourceOptions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x))
            {
                targetOptions.Add(option);
            }
        }

        private void ApplyFilter()
        {
            var query = SearchText?.Trim() ?? string.Empty;

            var filtered = allExercises.Where(exercise =>
                (string.IsNullOrWhiteSpace(query) || exercise.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
                (SelectedBodyPart == AllOption || exercise.BodyPart.Equals(SelectedBodyPart, StringComparison.CurrentCultureIgnoreCase)) &&
                (SelectedEquipment == AllOption || exercise.Equipment.Equals(SelectedEquipment, StringComparison.CurrentCultureIgnoreCase)) &&
                (SelectedLevel == AllOption || exercise.Level.Equals(SelectedLevel, StringComparison.CurrentCultureIgnoreCase)));

            FilteredExercises.Clear();

            foreach (var exercise in filtered)
            {
                FilteredExercises.Add(exercise);
            }
        }
    }
}
