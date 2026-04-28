using CommunityToolkit.Mvvm.ComponentModel;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using Microsoft.Maui.ApplicationModel;
using System.Collections.ObjectModel;
using System.Threading;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class ExerciseSelectionViewModel : ObservableObject
    {
        private const string AllOption = "Tümü";
        private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(180);

        private readonly ExerciseCatalogProvider _exerciseCatalogProvider;
        private readonly List<ExerciseCatalogItemDto> allExercises = [];
        private bool _isInitialized;
        private bool _suspendFiltering;
        private CancellationTokenSource? _searchDebounceCts;
        private int _filterVersion;
        private string _lastAppliedFilterKey = string.Empty;

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string selectedBodyPart = AllOption;

        [ObservableProperty]
        private string selectedEquipment = AllOption;

        [ObservableProperty]
        private string selectedLevel = AllOption;

        private IReadOnlyList<ExerciseCatalogItemDto> filteredExercises = [];

        public string BodyPartDisplayText => IsFilterInactive(SelectedBodyPart) ? "Bölge" : SelectedBodyPart;

        public string EquipmentDisplayText => IsFilterInactive(SelectedEquipment) ? "Ekipman" : SelectedEquipment;

        public string LevelDisplayText => IsFilterInactive(SelectedLevel) ? "Seviye" : SelectedLevel;

        public bool IsBodyPartFiltered => !IsFilterInactive(SelectedBodyPart);

        public bool IsEquipmentFiltered => !IsFilterInactive(SelectedEquipment);

        public bool IsLevelFiltered => !IsFilterInactive(SelectedLevel);

        public ObservableCollection<string> BodyPartOptions { get; } = new();

        public ObservableCollection<string> EquipmentOptions { get; } = new();

        public ObservableCollection<string> LevelOptions { get; } = new();

        public IReadOnlyList<ExerciseCatalogItemDto> FilteredExercises
        {
            get => filteredExercises;
            private set => SetProperty(ref filteredExercises, value);
        }

        public ExerciseSelectionViewModel(ExerciseCatalogProvider exerciseCatalogProvider)
        {
            _exerciseCatalogProvider = exerciseCatalogProvider;
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            var catalog = await _exerciseCatalogProvider.GetCatalogAsync();

            allExercises.Clear();
            allExercises.AddRange(catalog);

            _suspendFiltering = true;

            SearchText = string.Empty;

            InitializeFilterOptions();

            SelectedBodyPart = BodyPartOptions.FirstOrDefault() ?? AllOption;
            SelectedEquipment = EquipmentOptions.FirstOrDefault() ?? AllOption;
            SelectedLevel = LevelOptions.FirstOrDefault() ?? AllOption;

            _suspendFiltering = false;
            _isInitialized = true;

            ApplyFilter();
        }

        partial void OnSearchTextChanged(string value)
        {
            if (_suspendFiltering || !_isInitialized)
            {
                return;
            }

            _ = DebouncedApplyFilterAsync();
        }

        partial void OnSelectedBodyPartChanged(string value)
        {
            OnPropertyChanged(nameof(BodyPartDisplayText));
            OnPropertyChanged(nameof(IsBodyPartFiltered));
            ApplyFilterIfReady();
        }

        partial void OnSelectedEquipmentChanged(string value)
        {
            OnPropertyChanged(nameof(EquipmentDisplayText));
            OnPropertyChanged(nameof(IsEquipmentFiltered));
            ApplyFilterIfReady();
        }

        partial void OnSelectedLevelChanged(string value)
        {
            OnPropertyChanged(nameof(LevelDisplayText));
            OnPropertyChanged(nameof(IsLevelFiltered));
            ApplyFilterIfReady();
        }

        private async Task DebouncedApplyFilterAsync()
        {
            _searchDebounceCts?.Cancel();
            _searchDebounceCts?.Dispose();

            var cts = new CancellationTokenSource();
            _searchDebounceCts = cts;

            try
            {
                await Task.Delay(SearchDebounceDelay, cts.Token);

                if (cts.IsCancellationRequested)
                {
                    return;
                }

                ApplyFilterIfReady();
            }
            catch (TaskCanceledException)
            {
            }
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

        private void ApplyFilterIfReady()
        {
            if (_suspendFiltering || !_isInitialized)
            {
                return;
            }

            var filterKey = CreateFilterKey();
            if (filterKey == _lastAppliedFilterKey)
            {
                return;
            }

            var version = Interlocked.Increment(ref _filterVersion);
            _ = ApplyFilterAsync(filterKey, version);
        }

        private void ApplyFilter()
        {
            var filterKey = CreateFilterKey();
            var filtered = BuildFilteredExercises(
                SearchText,
                SelectedBodyPart,
                SelectedEquipment,
                SelectedLevel);

            if (filterKey == _lastAppliedFilterKey && HasSameResults(filtered))
            {
                return;
            }

            _lastAppliedFilterKey = filterKey;
            FilteredExercises = filtered;
        }

        private async Task ApplyFilterAsync(string filterKey, int version)
        {
            var searchText = SearchText;
            var bodyPart = SelectedBodyPart;
            var equipment = SelectedEquipment;
            var level = SelectedLevel;

            var filtered = await Task.Run(() => BuildFilteredExercises(
                searchText,
                bodyPart,
                equipment,
                level));

            if (version != Volatile.Read(ref _filterVersion))
            {
                return;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (version != _filterVersion)
                {
                    return;
                }

                if (filterKey == _lastAppliedFilterKey && HasSameResults(filtered))
                {
                    return;
                }

                _lastAppliedFilterKey = filterKey;
                FilteredExercises = filtered;
            });
        }

        private IReadOnlyList<ExerciseCatalogItemDto> BuildFilteredExercises(
            string? searchText,
            string? bodyPart,
            string? equipment,
            string? level)
        {
            var query = searchText?.Trim() ?? string.Empty;
            var selectedBodyPart = NormalizeFilterValue(bodyPart);
            var selectedEquipment = NormalizeFilterValue(equipment);
            var selectedLevel = NormalizeFilterValue(level);

            return allExercises.Where(exercise =>
                (string.IsNullOrWhiteSpace(query) || exercise.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
                (IsFilterInactive(selectedBodyPart) || exercise.BodyPart.Equals(selectedBodyPart, StringComparison.CurrentCultureIgnoreCase)) &&
                (IsFilterInactive(selectedEquipment) || exercise.Equipment.Equals(selectedEquipment, StringComparison.CurrentCultureIgnoreCase)) &&
                (IsFilterInactive(selectedLevel) || exercise.Level.Equals(selectedLevel, StringComparison.CurrentCultureIgnoreCase)))
                .ToArray();
        }

        private bool HasSameResults(IReadOnlyList<ExerciseCatalogItemDto> next)
        {
            if (FilteredExercises.Count != next.Count)
            {
                return false;
            }

            for (var i = 0; i < next.Count; i++)
            {
                if (FilteredExercises[i].Id != next[i].Id)
                {
                    return false;
                }
            }

            return true;
        }

        private string CreateFilterKey()
        {
            return string.Join("|",
                SearchText?.Trim() ?? string.Empty,
                NormalizeFilterValue(SelectedBodyPart),
                NormalizeFilterValue(SelectedEquipment),
                NormalizeFilterValue(SelectedLevel));
        }

        private static bool IsFilterInactive(string? value)
        {
            return string.IsNullOrWhiteSpace(value) || value.Equals(AllOption, StringComparison.CurrentCultureIgnoreCase);
        }

        private static string NormalizeFilterValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? AllOption : value.Trim();
        }
    }
}
