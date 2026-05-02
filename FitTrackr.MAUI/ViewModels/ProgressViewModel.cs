using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Models;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using Microsoft.Maui.Graphics;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;

namespace FitTrackr.MAUI.ViewModels
{
    /// <summary>
    /// Displays workout progress metrics and charts.
    /// 
    /// FIXES APPLIED:
    /// 1. MESSENGER SUBSCRIPTION: Subscribes to ExerciseAddedMessage for real-time updates
    /// 2. OPTIMISTIC UPDATE: Updates _allEntries immediately when exercise is added (no 2-3 min delay)
    /// 3. DATE NORMALIZATION: Uses ToLocalDate() consistently to prevent timezone bugs
    /// 4. DEDUPLICATION: Tracks last processed message to prevent re-processing
    /// 5. EVENT-DRIVEN: Deterministic state updates via messenger pattern
    /// </summary>
    public partial class ProgressViewModel : ObservableObject
    {
        private const string TimeFilterFallbackText = "Dönem Seç";
        private const string ExerciseFallbackText = "Egzersiz Seç";
        private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

        private readonly WorkoutService _workoutService;
        private readonly ExerciseService _exerciseService;
        private readonly List<ProgressWorkoutEntry> _allEntries = new();
        private readonly RelayCommand<ProgressTimeRangeOption> _selectTimeFilterCommand;
        private readonly RelayCommand<ProgressExerciseOption> _selectExerciseCommand;
        private readonly RelayCommand<ProgressMetricOption> _selectMetricCommand;

        private bool _isInitialized;
        private bool _isRefreshing;
        private ProgressMetricOption? _selectedMetric;
        private IDrawable _performanceChartDrawable = new ProgressLineChartDrawable(Array.Empty<double>(), -1, -1);
        private string _chartMaxLabel = "—";
        private string _chartMidLabel = "—";
        private string _chartMinLabel = "0";

        // DEDUPLICATION: Track last processed exercise added message to prevent re-processing
        private Guid? _lastExerciseAddedWorkoutId;

        public ObservableCollection<ProgressTimeRangeOption> TimeFilters { get; } = new();
        public ObservableCollection<ProgressExerciseOption> ExerciseFilters { get; } = new();
        public ObservableCollection<ProgressMetricOption> MetricOptions { get; } = new();
        public ObservableCollection<ProgressChartPoint> PerformanceChartPoints { get; } = new();
        public ObservableCollection<ProgressInsightItem> Insights { get; } = new();

        [ObservableProperty]
        private ProgressTimeRangeOption? selectedTimeFilter;

        [ObservableProperty]
        private ProgressExerciseOption? selectedExercise;

        [ObservableProperty]
        private string selectedRangeDateDisplay = "—";

        [ObservableProperty]
        private string comparisonCurrentValueText = "—";

        [ObservableProperty]
        private string comparisonDifferenceText = "—";

        [ObservableProperty]
        private string comparisonCurrentUnitText = string.Empty;

        [ObservableProperty]
        private string comparisonDifferenceUnitText = string.Empty;

        public ProgressMetricOption? SelectedMetric
        {
            get => _selectedMetric;
            set
            {
                if (SetProperty(ref _selectedMetric, value))
                {
                    UpdateSelectionStates();
                    RefreshDashboard();
                }
            }
        }

        public IDrawable PerformanceChartDrawable
        {
            get => _performanceChartDrawable;
            set => SetProperty(ref _performanceChartDrawable, value);
        }

        public string ChartMaxLabel
        {
            get => _chartMaxLabel;
            set => SetProperty(ref _chartMaxLabel, value);
        }

        public string ChartMidLabel
        {
            get => _chartMidLabel;
            set => SetProperty(ref _chartMidLabel, value);
        }

        public string ChartMinLabel
        {
            get => _chartMinLabel;
            set => SetProperty(ref _chartMinLabel, value);
        }

        public string SelectedPeriodTypeDisplay => SelectedTimeFilter?.Title ?? TimeFilterFallbackText;
        public string SelectedExerciseDisplay => SelectedExercise?.Title ?? ExerciseFallbackText;
        public bool HasChartData => PerformanceChartPoints.Count > 0;
        public bool ShowChartEmptyState => !HasChartData;
        public bool HasWorkoutData => _allEntries.Count > 0;
        public bool HasExercisesInRange => ExerciseFilters.Count > 0;
        public bool ShowExerciseEmptyState => !HasExercisesInRange;
        public string ExerciseEmptyStateText => $"{SelectedPeriodTypeDisplay} döneminde kayıtlı egzersiz bulunamadı.";

        public ICommand SelectTimeFilterCommand => _selectTimeFilterCommand;
        public ICommand SelectExerciseCommand => _selectExerciseCommand;
        public ICommand SelectMetricCommand => _selectMetricCommand;

        public ProgressViewModel(
            WorkoutService workoutService,
            ExerciseService exerciseService)
        {
            _workoutService = workoutService;
            _exerciseService = exerciseService;

            _selectTimeFilterCommand = new RelayCommand<ProgressTimeRangeOption>(SelectTimeFilter);
            _selectExerciseCommand = new RelayCommand<ProgressExerciseOption>(SelectExercise);
            _selectMetricCommand = new RelayCommand<ProgressMetricOption>(SelectMetric);

            BuildTimeFilters();
            BuildMetricOptions();
            SelectedTimeFilter = TimeFilters.FirstOrDefault();
            SelectedMetric = MetricOptions.FirstOrDefault();
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;

            // FIX #2: SUBSCRIBE TO EXERCISE ADDED MESSAGE FOR REAL-TIME UPDATES
            // This enables immediate dashboard refresh when exercise is added (no 2-3 min delay)
            WeakReferenceMessenger.Default.Register<ExerciseAddedMessage>(this, OnExerciseAdded);

            await LoadProgressAsync();
        }

        public Task RefreshAsync()
        {
            return LoadProgressAsync();
        }

        /// <summary>
        /// FIX #2 & #4: Handles real-time exercise added message.
        /// Updates _allEntries immediately and refreshes dashboard.
        /// Deduplicates messages using workout ID tracking to prevent race conditions.
        /// </summary>
        private async void OnExerciseAdded(object recipient, ExerciseAddedMessage message)
        {
            try
            {
                // FIX #4: DEDUPLICATION GUARD
                // Prevent re-processing the same message if it arrives multiple times
                if (_lastExerciseAddedWorkoutId == message.Value)
                {
                    return;
                }

                _lastExerciseAddedWorkoutId = message.Value;

                // FIX #2: OPTIMISTIC UPDATE
                // Fetch the updated workout with new exercise data
                var workout = await _workoutService.GetWorkoutByIdAsync(message.Value);
                if (workout == null)
                {
                    return;
                }

                // Load entries for this workout
                var newEntries = await LoadWorkoutEntriesAsync(new WorkoutSummaryDto
                {
                    Id = workout.Id,
                    WorkoutName = workout.WorkoutName,
                    WorkoutDate = workout.WorkoutDate
                });

                if (newEntries.Count == 0)
                {
                    return;
                }

                // FIX #3: UPDATE _allEntries WITH NORMALIZED DATES
                // Remove old entries for this local date, add new ones
                var localDate = message.WorkoutDate; // Already normalized by ExerciseAddedMessage

                // Remove duplicates by date and exercise name
                _allEntries.RemoveAll(x => x.Date == localDate &&
                    newEntries.Any(ne => ne.ExerciseName.Equals(x.ExerciseName, StringComparison.OrdinalIgnoreCase)));

                _allEntries.AddRange(newEntries);

                // FIX #2: IMMEDIATE REFRESH (NO DELAY)
                // Trigger dashboard update immediately on UI thread
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RefreshDashboard();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnExerciseAdded error: {ex.Message}");
            }
            finally
            {
                // Reset deduplication tracker after delay to allow message to complete processing
                await Task.Delay(1000);
                _lastExerciseAddedWorkoutId = null;
            }
        }

        partial void OnSelectedTimeFilterChanged(ProgressTimeRangeOption? value)
        {
            UpdateSelectionStates();
            OnPropertyChanged(nameof(SelectedPeriodTypeDisplay));
            OnPropertyChanged(nameof(ExerciseEmptyStateText));
            RefreshDashboard();
        }

        partial void OnSelectedExerciseChanged(ProgressExerciseOption? value)
        {
            UpdateSelectionStates();
            OnPropertyChanged(nameof(SelectedExerciseDisplay));
            RefreshDashboard();
        }

        private void BuildTimeFilters()
        {
            TimeFilters.Clear();
            TimeFilters.Add(new ProgressTimeRangeOption("1 Ay", ProgressTimeRange.OneMonth));
            TimeFilters.Add(new ProgressTimeRangeOption("3 Ay", ProgressTimeRange.ThreeMonths));
            TimeFilters.Add(new ProgressTimeRangeOption("Tümü", ProgressTimeRange.AllTime));
        }

        private void BuildMetricOptions()
        {
            MetricOptions.Clear();
            MetricOptions.Add(new ProgressMetricOption("Ağırlık", ProgressMetricType.Weight));
            MetricOptions.Add(new ProgressMetricOption("Antrenman Hacmi", ProgressMetricType.Volume));
            MetricOptions.Add(new ProgressMetricOption("Tahmini 1RM", ProgressMetricType.EstimatedOneRm));
        }

        private async Task LoadProgressAsync()
        {
            try
            {
                var workoutSummaries = await _workoutService.GetWorkoutsAsync();
                var workoutTasks = workoutSummaries
                    .OrderByDescending(x => x.WorkoutDate)
                    .Select(LoadWorkoutEntriesAsync)
                    .ToList();

                var workoutEntryGroups = await Task.WhenAll(workoutTasks);
                _allEntries.Clear();
                _allEntries.AddRange(workoutEntryGroups.SelectMany(x => x));

                RefreshDashboard();
            }
            catch (Exception ex)
            {
                ExerciseFilters.Clear();
                SelectedExercise = null;
                PerformanceChartPoints.Clear();
                Insights.Clear();
                PerformanceChartDrawable = new ProgressLineChartDrawable(Array.Empty<double>(), -1, -1);
                ChartMaxLabel = "—";
                ChartMidLabel = "—";
                ChartMinLabel = "0";
                SelectedRangeDateDisplay = "—";
                ComparisonCurrentValueText = "—";
                ComparisonDifferenceText = "—";
                ComparisonCurrentUnitText = string.Empty;
                ComparisonDifferenceUnitText = string.Empty;

                Insights.Add(new ProgressInsightItem($"Veri yüklenemedi: {ex.Message}", Color.FromArgb("#FF7043")));
                OnPropertyChanged(nameof(HasChartData));
                OnPropertyChanged(nameof(ShowChartEmptyState));
                OnPropertyChanged(nameof(HasWorkoutData));
                OnPropertyChanged(nameof(HasExercisesInRange));
                OnPropertyChanged(nameof(ShowExerciseEmptyState));
            }
        }

        private async Task<List<ProgressWorkoutEntry>> LoadWorkoutEntriesAsync(WorkoutSummaryDto workoutSummary)
        {
            try
            {
                var workout = await _workoutService.GetWorkoutByIdAsync(workoutSummary.Id);
                var entries = new List<ProgressWorkoutEntry>();

                if (workout.Exercises == null || workout.Exercises.Count == 0)
                {
                    return entries;
                }

                // FIX #3: NORMALIZE DATES USING ToLocalDate()
                // Backend returns UTC, convert to local for consistent display/grouping
                var workoutLocalDate = workout.WorkoutDate.ToLocalDate();

                foreach (var exerciseSummary in workout.Exercises)
                {
                    var exercise = await _exerciseService.GetExerciseByIdAsync(exerciseSummary.Id);
                    var sets = exercise.ExerciseSets?.OrderBy(s => s.SetNumber).ToList() ?? [];
                    if (sets.Count == 0)
                    {
                        continue;
                    }

                    var bestSet = GetBestSetDto(sets);
                    var totalVolume = sets.Sum(s => s.WeightInKg * Math.Max(TryParseReps(s.Reps), 0));
                    var bestOneRm = sets.Max(s => CalculateEstimatedOneRm(s.WeightInKg, TryParseReps(s.Reps)));

                    entries.Add(new ProgressWorkoutEntry(
                        workoutLocalDate,  // FIX #3: Use ToLocalDate() instead of raw .Date
                        exercise.ExerciseName ?? string.Empty,
                        bestSet.WeightInKg,
                        totalVolume,
                        bestOneRm));
                }

                return entries;
            }
            catch
            {
                return [];
            }
        }

        private void RefreshDashboard()
        {
            if (_isRefreshing)
            {
                return;
            }

            _isRefreshing = true;

            try
            {
                if (_allEntries.Count == 0)
                {
                    ExerciseFilters.Clear();
                    SelectedExercise = null;
                    PerformanceChartPoints.Clear();
                    Insights.Clear();
                    PerformanceChartDrawable = new ProgressLineChartDrawable(Array.Empty<double>(), -1, -1);
                    ChartMaxLabel = "—";
                    ChartMidLabel = "—";
                    ChartMinLabel = "0";
                    SelectedRangeDateDisplay = "—";
                    ComparisonCurrentValueText = "—";
                    ComparisonDifferenceText = "—";
                    ComparisonCurrentUnitText = string.Empty;
                    ComparisonDifferenceUnitText = string.Empty;
                    Insights.Add(new ProgressInsightItem("Antrenman eklediğinde raporlar burada görünecek.", Color.FromArgb("#FF7043")));
                    OnPropertyChanged(nameof(HasChartData));
                    OnPropertyChanged(nameof(ShowChartEmptyState));
                    OnPropertyChanged(nameof(HasWorkoutData));
                    OnPropertyChanged(nameof(HasExercisesInRange));
                    OnPropertyChanged(nameof(ShowExerciseEmptyState));
                    return;
                }

                var rangeEntries = GetEntriesForSelectedRange().ToList();
                BuildExerciseFilters(rangeEntries);
                EnsureSelectedExercise();
                UpdateExerciseSubtitles(rangeEntries);

                var selectedExerciseEntries = GetSelectedExerciseEntries(rangeEntries).ToList();
                var comparison = BuildComparisonSnapshot(selectedExerciseEntries);
                UpdateComparisonVisuals(comparison);
                UpdateComparisonInsights(comparison);

                OnPropertyChanged(nameof(HasChartData));
                OnPropertyChanged(nameof(ShowChartEmptyState));
                OnPropertyChanged(nameof(HasWorkoutData));
                OnPropertyChanged(nameof(HasExercisesInRange));
                OnPropertyChanged(nameof(ShowExerciseEmptyState));
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void BuildExerciseFilters(List<ProgressWorkoutEntry> rangeEntries)
        {
            var existingSelection = SelectedExercise?.Title;
            ExerciseFilters.Clear();

            foreach (var group in rangeEntries
                .Where(x => !string.IsNullOrWhiteSpace(x.ExerciseName))
                .GroupBy(x => x.ExerciseName)
                .OrderByDescending(g => g.Max(x => x.Date))
                .ThenByDescending(g => g.Count())
                .ThenBy(g => g.Key))
            {
                ExerciseFilters.Add(new ProgressExerciseOption(group.Key, string.Empty));
            }

            if (!string.IsNullOrWhiteSpace(existingSelection))
            {
                SelectedExercise = ExerciseFilters.FirstOrDefault(x => x.Title.Equals(existingSelection, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void EnsureSelectedExercise()
        {
            if (ExerciseFilters.Count == 0)
            {
                SelectedExercise = null;
                return;
            }

            if (SelectedExercise != null && ExerciseFilters.Any(x => x.Equals(SelectedExercise)))
            {
                SelectedExercise = ExerciseFilters.First(x => x.Equals(SelectedExercise));
                return;
            }

            SelectedExercise = ExerciseFilters.FirstOrDefault();
        }

        private void UpdateExerciseSubtitles(List<ProgressWorkoutEntry> rangeEntries)
        {
            foreach (var exercise in ExerciseFilters)
            {
                var exerciseEntries = rangeEntries
                    .Where(x => x.ExerciseName.Equals(exercise.Title, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Date)
                    .ToList();

                if (exerciseEntries.Count == 0)
                {
                    exercise.Subtitle = "Kayıt yok";
                    continue;
                }

                var lastDate = exerciseEntries[^1].Date;
                exercise.Subtitle = $"{exerciseEntries.Count} kayıt • son {lastDate.ToString("dd MMM", TurkishCulture)}";
            }
        }

        private ProgressComparisonSnapshot BuildComparisonSnapshot(List<ProgressWorkoutEntry> selectedExerciseEntries)
        {
            if (selectedExerciseEntries.Count == 0)
            {
                return ProgressComparisonSnapshot.Empty(SelectedMetric?.Type ?? ProgressMetricType.Weight, SelectedTimeFilter?.Range ?? ProgressTimeRange.OneMonth);
            }

            var metricType = SelectedMetric?.Type ?? ProgressMetricType.Weight;
            var rangeType = SelectedTimeFilter?.Range ?? ProgressTimeRange.OneMonth;
            var orderedEntries = selectedExerciseEntries.OrderBy(x => x.Date).ToList();

            return rangeType == ProgressTimeRange.AllTime
                ? BuildAllTimeComparison(orderedEntries, metricType, rangeType)
                : BuildRecentComparison(orderedEntries, metricType, rangeType);
        }

        private ProgressComparisonSnapshot BuildRecentComparison(
            List<ProgressWorkoutEntry> orderedEntries,
            ProgressMetricType metricType,
            ProgressTimeRange rangeType)
        {
            var firstEntry = orderedEntries.First();
            var currentEntry = orderedEntries[^1];

            var previousValue = GetMetricValue(firstEntry, metricType);
            var currentValue = GetMetricValue(currentEntry, metricType);
            var difference = currentValue - previousValue;
            var rangeDisplay = $"{firstEntry.Date.ToString("dd MMM yyyy", TurkishCulture)} - {currentEntry.Date.ToString("dd MMM yyyy", TurkishCulture)}";

            var points = BuildRangeChartPoints(orderedEntries, metricType);

            return new ProgressComparisonSnapshot(points, previousValue, currentValue, difference, rangeDisplay, metricType, rangeType);
        }

        private ProgressComparisonSnapshot BuildAllTimeComparison(
            List<ProgressWorkoutEntry> orderedEntries,
            ProgressMetricType metricType,
            ProgressTimeRange rangeType)
        {
            var firstEntry = orderedEntries.First();
            var bestEntry = orderedEntries
                .OrderByDescending(x => GetMetricValue(x, metricType))
                .ThenByDescending(x => x.Date)
                .First();

            var previousValue = GetMetricValue(firstEntry, metricType);
            var currentValue = GetMetricValue(bestEntry, metricType);
            var difference = currentValue - previousValue;
            var rangeDisplay = $"{firstEntry.Date.ToString("dd MMM yyyy", TurkishCulture)} - {bestEntry.Date.ToString("dd MMM yyyy", TurkishCulture)}";

            var monthlyPoints = BuildAllTimeMonthlyChartPoints(orderedEntries, metricType);

            return new ProgressComparisonSnapshot(monthlyPoints, previousValue, currentValue, difference, rangeDisplay, metricType, rangeType);
        }

        private static List<ProgressChartPoint> BuildRangeChartPoints(List<ProgressWorkoutEntry> orderedEntries, ProgressMetricType metricType)
        {
            if (orderedEntries.Count == 0)
            {
                return [];
            }

            var values = orderedEntries.Select(x => GetMetricValue(x, metricType)).ToList();
            var maxValue = values.Max();

            return orderedEntries
                .Select((entry, index) =>
                {
                    var value = values[index];
                    var isPeak = Math.Abs(value - maxValue) < 0.001;
                    var isLast = index == orderedEntries.Count - 1;

                    return new ProgressChartPoint(
                        entry.Date.ToString("dd MMM", TurkishCulture),
                        FormatMetricValue(value, metricType),
                        value,
                        isLast,
                        isPeak);
                })
                .ToList();
        }

        private static List<ProgressChartPoint> BuildAllTimeMonthlyChartPoints(List<ProgressWorkoutEntry> orderedEntries, ProgressMetricType metricType)
        {
            if (orderedEntries.Count == 0)
            {
                return [];
            }

            var monthlyBestEntries = orderedEntries
                .GroupBy(x => new DateTime(x.Date.Year, x.Date.Month, 1))
                .Select(group => group
                    .OrderByDescending(entry => GetMetricValue(entry, metricType))
                    .ThenByDescending(entry => entry.Date)
                    .First())
                .OrderBy(x => x.Date)
                .ToList();

            var values = monthlyBestEntries.Select(x => GetMetricValue(x, metricType)).ToList();
            var maxValue = values.Max();

            return monthlyBestEntries
                .Select((entry, index) =>
                {
                    var value = values[index];
                    var isPeak = Math.Abs(value - maxValue) < 0.001;
                    var isLast = index == monthlyBestEntries.Count - 1;

                    return new ProgressChartPoint(
                        entry.Date.ToString("MMM yy", TurkishCulture),
                        FormatMetricValue(value, metricType),
                        value,
                        isLast,
                        isPeak);
                })
                .ToList();
        }

        private void UpdateComparisonVisuals(ProgressComparisonSnapshot comparison)
        {
            PerformanceChartPoints.Clear();

            if (!comparison.HasData)
            {
                PerformanceChartDrawable = new ProgressLineChartDrawable(Array.Empty<double>(), -1, -1);
                ChartMaxLabel = "—";
                ChartMidLabel = "—";
                ChartMinLabel = "0";
                SelectedRangeDateDisplay = "—";
                ComparisonCurrentValueText = "—";
                ComparisonDifferenceText = "—";
                ComparisonCurrentUnitText = string.Empty;
                ComparisonDifferenceUnitText = string.Empty;
                return;
            }

            foreach (var point in comparison.Points)
            {
                PerformanceChartPoints.Add(point);
            }

            var values = comparison.Points.Select(x => x.Value).ToList();
            var maxValue = values.Max();
            var minValue = values.Min();
            var peakIndex = values.FindIndex(x => Math.Abs(x - maxValue) < 0.001);

            PerformanceChartDrawable = new ProgressLineChartDrawable(values, values.Count - 1, peakIndex);
            ChartMaxLabel = FormatMetricValue(maxValue, comparison.MetricType);
            ChartMidLabel = FormatMetricValue((maxValue + minValue) / 2d, comparison.MetricType);
            ChartMinLabel = FormatMetricValue(minValue, comparison.MetricType);
            SelectedRangeDateDisplay = comparison.RangeDisplay;
            ComparisonCurrentValueText = FormatHeaderNumberValue(comparison.CurrentValue, comparison.MetricType, false);
            ComparisonDifferenceText = FormatHeaderNumberValue(comparison.Difference, comparison.MetricType, true);
            ComparisonCurrentUnitText = GetHeaderUnitText(comparison.MetricType);
            ComparisonDifferenceUnitText = GetHeaderUnitText(comparison.MetricType);
        }

        private void UpdateComparisonInsights(ProgressComparisonSnapshot comparison)
        {
            Insights.Clear();

            if (!comparison.HasData)
            {
                Insights.Add(new ProgressInsightItem("Karşılaştırma için yeterli kayıt yok.", Color.FromArgb("#FF8A65")));
                return;
            }

            if (Math.Abs(comparison.Difference) < 0.001)
            {
                Insights.Add(new ProgressInsightItem("Seçili aralıkta belirgin değişim görünmüyor.", Color.FromArgb("#F59E0B")));
                return;
            }

            var diffText = FormatSignedMetricValue(comparison.Difference, comparison.MetricType);
            var periodText = comparison.RangeType == ProgressTimeRange.AllTime
                ? "İlk kayda göre"
                : comparison.RangeType == ProgressTimeRange.ThreeMonths
                    ? "3 aya göre"
                    : "1 aya göre";

            Insights.Add(new ProgressInsightItem($"{periodText} {diffText} değişim var.", Color.FromArgb("#FF8A65")));

            if (comparison.MetricType == ProgressMetricType.Volume && comparison.Difference > 0)
            {
                Insights.Add(new ProgressInsightItem("Toplam antrenman hacmi yükseliş trendinde.", Color.FromArgb("#FB923C")));
            }
        }

        private IEnumerable<ProgressWorkoutEntry> GetEntriesForSelectedRange()
        {
            var (start, end) = GetSelectedRangeBounds();
            return _allEntries.Where(x => x.Date >= start && x.Date <= end);
        }

        private List<ProgressWorkoutEntry> GetSelectedExerciseEntries(IEnumerable<ProgressWorkoutEntry> entries)
        {
            if (SelectedExercise is null)
            {
                return [];
            }

            return entries
                .Where(x => x.ExerciseName.Equals(SelectedExercise.Title, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private (DateTime Start, DateTime End) GetSelectedRangeBounds()
        {
            var end = DateTime.Today;
            var range = SelectedTimeFilter?.Range ?? ProgressTimeRange.OneMonth;

            if (range == ProgressTimeRange.AllTime)
            {
                var start = _allEntries.Count == 0 ? end : _allEntries.Min(x => x.Date);
                return (start, end);
            }

            if (range == ProgressTimeRange.ThreeMonths)
            {
                return (end.AddDays(-89), end);
            }

            return (end.AddDays(-29), end);
        }

        private void UpdateSelectionStates()
        {
            foreach (var filter in TimeFilters)
            {
                filter.IsSelected = SelectedTimeFilter != null && filter.Equals(SelectedTimeFilter);
            }

            foreach (var exercise in ExerciseFilters)
            {
                exercise.IsSelected = SelectedExercise != null && exercise.Equals(SelectedExercise);
            }

            foreach (var metric in MetricOptions)
            {
                metric.IsSelected = SelectedMetric != null && metric.Equals(SelectedMetric);
            }
        }

        private void SelectTimeFilter(ProgressTimeRangeOption? option)
        {
            if (option == null || option.Equals(SelectedTimeFilter))
            {
                return;
            }

            SelectedTimeFilter = option;
        }

        private void SelectExercise(ProgressExerciseOption? option)
        {
            if (option == null || option.Equals(SelectedExercise))
            {
                return;
            }

            SelectedExercise = option;
        }

        private void SelectMetric(ProgressMetricOption? option)
        {
            if (option == null || option.Equals(SelectedMetric))
            {
                return;
            }

            SelectedMetric = option;
        }

        private static ExerciseSetDto GetBestSetDto(IEnumerable<ExerciseSetDto> sets)
        {
            return sets
                .OrderByDescending(s => s.WeightInKg)
                .ThenByDescending(s => TryParseReps(s.Reps))
                .First();
        }

        private static int TryParseReps(string? reps)
        {
            return int.TryParse(reps, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        }

        private static double CalculateEstimatedOneRm(double weightKg, int reps)
        {
            if (weightKg <= 0 || reps <= 0)
            {
                return 0;
            }

            return weightKg * (1 + (reps / 30d));
        }

        private static double GetMetricValue(ProgressWorkoutEntry entry, ProgressMetricType metricType)
        {
            return metricType switch
            {
                ProgressMetricType.Weight => entry.BestWeightKg,
                ProgressMetricType.Volume => entry.TotalVolumeKg,
                ProgressMetricType.EstimatedOneRm => entry.BestEstimatedOneRm,
                _ => 0
            };
        }

        private static string FormatMetricValue(double value, ProgressMetricType metricType)
        {
            if (Math.Abs(value) < 0.001)
            {
                return metricType == ProgressMetricType.Weight ? "0 kg" : "0";
            }

            return metricType switch
            {
                ProgressMetricType.Weight => $"{value:0.#} kg",
                ProgressMetricType.Volume => $"{value:0}",
                ProgressMetricType.EstimatedOneRm => $"{value:0.#}",
                _ => $"{value:0.#}"
            };
        }

        private static string FormatSignedMetricValue(double value, ProgressMetricType metricType)
        {
            if (Math.Abs(value) < 0.001)
            {
                return "0";
            }

            var absolute = FormatMetricValue(Math.Abs(value), metricType);
            if (absolute == "—")
            {
                return "0";
            }

            return value > 0 ? $"+{absolute}" : $"-{absolute}";
        }

        private static string FormatHeaderNumberValue(double value, ProgressMetricType metricType, bool includeSign)
        {
            if (Math.Abs(value) < 0.001)
            {
                return "0";
            }

            var numeric = metricType == ProgressMetricType.Weight
                ? Math.Abs(value).ToString("0.#", TurkishCulture)
                : Math.Abs(value).ToString("0", TurkishCulture);

            if (!includeSign)
            {
                return numeric;
            }

            return value > 0 ? $"+{numeric}" : $"-{numeric}";
        }

        private static string GetHeaderUnitText(ProgressMetricType metricType)
        {
            return metricType == ProgressMetricType.Weight ? "kg" : string.Empty;
        }

        private sealed record ProgressWorkoutEntry(
            DateTime Date,
            string ExerciseName,
            double BestWeightKg,
            double TotalVolumeKg,
            double BestEstimatedOneRm);

        private sealed record ProgressComparisonSnapshot(
            IReadOnlyList<ProgressChartPoint> Points,
            double PreviousValue,
            double CurrentValue,
            double Difference,
            string RangeDisplay,
            ProgressMetricType MetricType,
            ProgressTimeRange RangeType)
        {
            public bool HasData => Points.Count > 0;

            public static ProgressComparisonSnapshot Empty(ProgressMetricType metricType, ProgressTimeRange rangeType)
            {
                return new ProgressComparisonSnapshot(Array.Empty<ProgressChartPoint>(), 0, 0, 0, "—", metricType, rangeType);
            }
        }
    }
}
