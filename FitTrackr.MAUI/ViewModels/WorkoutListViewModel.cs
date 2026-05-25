using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;

namespace FitTrackr.MAUI.ViewModels
{
    public class WorkoutListViewModel : ObservableObject
    {
        private readonly WorkoutService _workoutService;
        private readonly ExerciseService _exerciseService;

        // ID-based lookup: fast operations by ID
        private readonly Dictionary<Guid, WorkoutSummaryDto> _workoutsById = new();

        // Date-based grouping: display organization
        private readonly Dictionary<DateTime, List<WorkoutSummaryDto>> _workoutsByDate = new();

        private Guid? _selectedWorkoutId;
        private DateTime _selectedDate;

        /// <summary>
        /// ExerciseAddedMessage geldiğinde true olur. WorkoutListPage OnAppearing'de
        /// tam reload'u atlamak için kullanılır — kart zaten güncellendi.
        /// </summary>
        private volatile bool _skipNextFullRefresh;

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (SetProperty(ref _selectedDate, value.Date))
                {
                    OnPropertyChanged(nameof(SelectedDateDisplay));
                    BuildWeekStrip();
                    _ = RefreshDailyWorkoutCardAsync();
                    _ = RefreshWeekWorkoutIndicatorsAsync();
                }
            }
        }

        private static readonly CultureInfo TurkishCulture = new("tr-TR");

        public string SelectedDateDisplay
        {
            get
            {
                var today = DateTime.Today;
                var selected = SelectedDate.Date;

                if (selected == today)
                    return "Bugün";

                if (selected == today.AddDays(-1))
                    return "Dün";

                if (selected == today.AddDays(1))
                    return "Yarın";

                return selected.ToString("dd MMMM dddd", TurkishCulture);
            }
        }

        public ObservableCollection<WeekDayItem> WeekDays { get; } = new();
        public ObservableCollection<DailyWorkoutCardViewModel> DailyWorkouts { get; } = new();

        public ICommand SelectDateCommand { get; }
        public ICommand GoToTodayCommand { get; }
        public ICommand ToggleWorkoutExpandCommand { get; }
        public IAsyncRelayCommand<DailyWorkoutCardViewModel> RenameWorkoutCommand { get; }
        public IAsyncRelayCommand<WorkoutExerciseItemViewModel> DeleteExerciseCommand { get; }

        public WorkoutListViewModel(WorkoutService workoutService, ExerciseService exerciseService)
        {
            _workoutService = workoutService;
            _exerciseService = exerciseService;

            SelectDateCommand = new RelayCommand<WeekDayItem>(OnDateSelected);
            GoToTodayCommand = new RelayCommand(() => SelectedDate = DateTime.Today);
            ToggleWorkoutExpandCommand = new RelayCommand<DailyWorkoutCardViewModel>(ToggleCardExpansion);
            RenameWorkoutCommand = new AsyncRelayCommand<DailyWorkoutCardViewModel>(RenameWorkoutAsync);
            DeleteExerciseCommand = new AsyncRelayCommand<WorkoutExerciseItemViewModel>(DeleteExerciseAsync);

            WeakReferenceMessenger.Default.Register<ExerciseAddedMessage>(this, OnExerciseAdded);

            SelectedDate = DateTime.Today;
        }

        /// <summary>
        /// Egzersiz kaydedildiğinde arka planda kart güncellenir; tam reload atlanır.
        /// </summary>
        private async void OnExerciseAdded(object recipient, ExerciseAddedMessage message)
        {
            // 1. Tam reload'u atla — bu metod zaten kartı güncelleyecek
            _skipNextFullRefresh = true;

            var date = message.WorkoutDate; // zaten local date

            // 2. O günün pill'ini hemen aç
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var dayItem = WeekDays.FirstOrDefault(d => d.Date == date);
                if (dayItem != null) dayItem.HasWorkout = true;
            });

            try
            {
                // 3. Sadece etkilenen workout'ın detayını çek (tüm GetAll yerine tek çağrı)
                var workoutDetail = await _workoutService.GetWorkoutByIdAsync(message.Value);
                var detailedExercises = new List<ExerciseDto>();

                foreach (var ex in workoutDetail.Exercises ?? [])
                {
                    try { detailedExercises.Add(await _exerciseService.GetExerciseByIdAsync(ex.Id)); }
                    catch { detailedExercises.Add(ex); }
                }

                // 4. _workoutsByDate'i güncelle (sonraki BuildWeekStrip için)
                if (_workoutsByDate.TryGetValue(date, out var dateWorkouts))
                {
                    var summary = dateWorkouts.FirstOrDefault(w => w.Id == message.Value);
                    if (summary != null)
                        summary.ExerciseCount = detailedExercises.Count;
                    else if (!dateWorkouts.Any(w => w.Id == message.Value))
                        dateWorkouts.Add(new WorkoutSummaryDto { Id = message.Value, WorkoutDate = workoutDetail.WorkoutDate, ExerciseCount = detailedExercises.Count });
                }
                else
                {
                    _workoutsByDate[date] = new List<WorkoutSummaryDto>
                    {
                        new() { Id = message.Value, WorkoutDate = workoutDetail.WorkoutDate, ExerciseCount = detailedExercises.Count }
                    };
                }

                // 5. Kartı güncelle (seçili gün bu tarihse)
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (SelectedDate.Date != date) return;

                    var card = DailyWorkouts.FirstOrDefault();
                    if (card == null) return;

                    if (!card.WorkoutId.HasValue || card.WorkoutId == Guid.Empty)
                        card.UpdateWorkoutId(message.Value);

                    card.SetExercises(detailedExercises);

                    // Seçili günün pill'ini kart durumuna göre kesinleştir
                    var dayItem = WeekDays.FirstOrDefault(d => d.Date == date);
                    if (dayItem != null) dayItem.HasWorkout = card.HasExercises;
                });
            }
            catch
            {
                // Hata olursa bir sonraki OnAppearing tam reload yapar
                _skipNextFullRefresh = false;
            }
        }

        /// <summary>
        /// OnAppearing'den çağrılır. true dönerse tam reload atlanmalı.
        /// </summary>
        public bool ConsumeSkipNextFullRefresh()
        {
            if (_skipNextFullRefresh)
            {
                _skipNextFullRefresh = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Loads all workouts and reorganizes them by ID and date.
        /// Provides both fast ID-based operations and date-based display.
        /// </summary>
        public async Task LoadWorkoutsAsync()
        {
            _workoutsById.Clear();
            _workoutsByDate.Clear();

            var workouts = await _workoutService.GetWorkoutsAsync();

            // Index by ID for fast operations
            foreach (var workout in workouts)
            {
                _workoutsById[workout.Id] = workout;
            }

            // Group by local date for display
            foreach (var workout in workouts)
            {
                var localDate = workout.WorkoutDate.ToLocalDate();

                if (!_workoutsByDate.ContainsKey(localDate))
                {
                    _workoutsByDate[localDate] = new List<WorkoutSummaryDto>();
                }

                _workoutsByDate[localDate].Add(workout);
            }

            BuildWeekStrip();
            await RefreshDailyWorkoutCardAsync();
            _ = RefreshWeekWorkoutIndicatorsAsync();
        }

        private void OnDateSelected(WeekDayItem? day)
        {
            if (day == null)
                return;

            SelectedDate = day.Date;
        }

        private void ToggleCardExpansion(DailyWorkoutCardViewModel? card)
        {
            if (card == null || !card.HasExercises)
                return;

            card.IsExpanded = !card.IsExpanded;
        }

        private async Task RenameWorkoutAsync(DailyWorkoutCardViewModel? card)
        {
            if (card == null)
                return;

            var currentName = string.IsNullOrWhiteSpace(card.WorkoutName)
                ? DailyWorkoutCardViewModel.DefaultWorkoutName
                : card.WorkoutName;

            var newName = await Shell.Current.DisplayPromptAsync(
                "Antrenman Adı",
                "Antrenman adını düzenle",
                accept: "Kaydet",
                cancel: "İptal",
                placeholder: "Antrenman adı",
                maxLength: 20,
                keyboard: Keyboard.Text,
                initialValue: currentName);

            if (newName == null)
                return;

            await SaveWorkoutNameAsync(card, newName);
        }

        private async Task DeleteExerciseAsync(WorkoutExerciseItemViewModel? exercise)
        {
            if (exercise == null)
            {
                return;
            }

            var card = DailyWorkouts.FirstOrDefault(w => w.Exercises.Any(e => e.ExerciseId == exercise.ExerciseId));
            if (card == null)
            {
                return;
            }

            // OPTİMİSTİK SİLME: Önce UI'dan kaldır, sonra API'ye gönder
            card.RemoveExercise(exercise.ExerciseId);

            // _workoutsByDate ExerciseCount güncelle
            if (card.WorkoutId.HasValue &&
                _workoutsByDate.TryGetValue(card.WorkoutDate.Date, out var dateWorkouts))
            {
                var summary = dateWorkouts.FirstOrDefault(w => w.Id == card.WorkoutId.Value);
                if (summary != null && summary.ExerciseCount > 0)
                    summary.ExerciseCount--;
            }

            // Egzersiz kalmadıysa pill'i kaldır
            if (!card.HasExercises)
            {
                var anyLeft = DailyWorkouts
                    .Where(c => c.WorkoutDate.Date == card.WorkoutDate.Date)
                    .Any(c => c.HasExercises);

                if (!anyLeft)
                {
                    var dayItem = WeekDays.FirstOrDefault(d => d.Date == card.WorkoutDate.Date);
                    if (dayItem != null) dayItem.HasWorkout = false;
                }
            }

            try
            {
                await _exerciseService.DeleteExerciseAsync(exercise.ExerciseId);
            }
            catch (Exception ex)
            {
                // Rollback: API başarısız oldu, kartı yenile
                await RefreshDailyWorkoutCardAsync();
                _ = RefreshWeekWorkoutIndicatorsAsync();
                await Shell.Current.DisplayAlert("Hata", $"Egzersiz silinemedi: {ex.Message}", "Tamam");
            }
        }

        private async Task SaveWorkoutNameAsync(DailyWorkoutCardViewModel card, string workoutName)
        {
            var normalizedName = string.IsNullOrWhiteSpace(workoutName)
                ? DailyWorkoutCardViewModel.DefaultWorkoutName
                : workoutName.Trim();

            try
            {
                if (card.HasPersistedWorkout && card.WorkoutId.HasValue)
                {
                    // Get current workout from backend to ensure date accuracy
                    var currentWorkout = await _workoutService.GetWorkoutByIdAsync(card.WorkoutId.Value);

                    var updatedWorkout = await _workoutService.UpdateWorkoutAsync(
                        card.WorkoutId.Value,
                        new UpdateWorkoutRequestDto
                        {
                            WorkoutName = normalizedName,
                            WorkoutDate = currentWorkout.WorkoutDate  // Use backend date, not cached date
                        });

                    SyncWorkout(updatedWorkout);
                    card.ApplyWorkout(updatedWorkout);
                    BuildWeekStrip();
                    _selectedWorkoutId = updatedWorkout.Id;
                    return;
                }

                var createdWorkout = await _workoutService.AddWorkoutAsync(new WorkoutRequestDto
                {
                    WorkoutName = normalizedName,
                    WorkoutDate = card.WorkoutDate,
                    DurationMinutes = 0,
                    LocationId = Guid.Empty
                });

                SyncWorkout(createdWorkout);
                card.ApplyWorkout(createdWorkout);
                card.UpdateExerciseState(false);
                _selectedWorkoutId = createdWorkout.Id;
                BuildWeekStrip();
                WeakReferenceMessenger.Default.Send(new WorkoutAddedMessage(createdWorkout));
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Hata", $"Antrenman adi guncellenemedi: {ex.Message}", "Tamam");
            }
        }

        private void SyncWorkout(WorkoutSummaryDto workout)
        {
            // Update ID index
            _workoutsById[workout.Id] = workout;

            var localDate = workout.WorkoutDate.ToLocalDate();

            // Update date index
            if (!_workoutsByDate.ContainsKey(localDate))
            {
                _workoutsByDate[localDate] = new List<WorkoutSummaryDto>();
            }

            var dateList = _workoutsByDate[localDate];
            var existingIdx = dateList.FindIndex(w => w.Id == workout.Id);

            if (existingIdx >= 0)
            {
                dateList[existingIdx] = workout;
            }
            else
            {
                dateList.Add(workout);
            }
        }

        private void BuildWeekStrip()
        {
            var weekStart = GetWeekStart(SelectedDate);
            var culture = new CultureInfo("tr-TR");

            WeekDays.Clear();

            for (var i = 0; i < 7; i++)
            {
                var day = weekStart.AddDays(i);
                WeekDays.Add(new WeekDayItem
                {
                    Date = day,
                    DayLabel = culture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek),
                    DayNumber = day.Day.ToString(culture),
                    IsSelected = day.Date == SelectedDate.Date,
                    HasWorkout = _workoutsByDate.TryGetValue(day.Date, out var dayWorkouts) && dayWorkouts.Any(w => w.ExerciseCount > 0)
                });
            }
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.Date.AddDays(-diff);
        }

        /// <summary>
        /// Refreshes the daily workout card for the selected date.
        /// Uses ID-based selection for reliability and date-based filtering for display.
        /// </summary>
        private async Task RefreshDailyWorkoutCardAsync()
        {
            DailyWorkouts.Clear();

            var selectedDate = SelectedDate.Date;

            // Get workouts for this local date
            var workoutsForDay = _workoutsByDate.TryGetValue(selectedDate, out var workouts)
                ? workouts
                : [];

            if (workoutsForDay.Count == 0)
            {
                var emptyCard = new DailyWorkoutCardViewModel(selectedDate, null);
                emptyCard.SetExercises([]);
                DailyWorkouts.Add(emptyCard);
                return;
            }

            // Select by ID: prefer previously selected workout, fall back to first
            var selectedWorkout = workoutsForDay
                .FirstOrDefault(w => w.Id == _selectedWorkoutId)
                ?? workoutsForDay.First();

            _selectedWorkoutId = selectedWorkout.Id;

            var card = new DailyWorkoutCardViewModel(selectedDate, selectedWorkout);
            DailyWorkouts.Add(card);

            try
            {
                // Always load details by ID for accuracy
                var workoutDetail = await _workoutService.GetWorkoutByIdAsync(selectedWorkout.Id);
                var detailedExercises = new List<ExerciseDto>();

                foreach (var exercise in workoutDetail.Exercises ?? [])
                {
                    try
                    {
                        detailedExercises.Add(await _exerciseService.GetExerciseByIdAsync(exercise.Id));
                    }
                    catch
                    {
                        detailedExercises.Add(exercise);
                    }
                }

                card.SetExercises(detailedExercises);

                // Seçili günün pill indikatörünü gerçek egzersiz sayısına göre güncelle
                var dayItem = WeekDays.FirstOrDefault(d => d.Date == selectedDate);
                if (dayItem != null)
                    dayItem.HasWorkout = card.HasExercises;
            }
            catch
            {
                card.SetExercises([]);
            }
        }

        /// <summary>
        /// Haftalık şeritteki tüm günler için egzersiz varlığını arka planda kontrol eder.
        /// Backend summary'de ExerciseCount olmasa da doğru çalışır (GetById ile doğrular).
        /// </summary>
        private async Task RefreshWeekWorkoutIndicatorsAsync()
        {
            var tasks = WeekDays
                .Where(d => _workoutsByDate.ContainsKey(d.Date) && _workoutsByDate[d.Date].Count > 0)
                .Select(async dayItem =>
                {
                    var workoutsForDay = _workoutsByDate[dayItem.Date];
                    var hasExercises = false;

                    foreach (var workout in workoutsForDay)
                    {
                        try
                        {
                            var detail = await _workoutService.GetWorkoutByIdAsync(workout.Id);
                            if (detail.Exercises != null && detail.Exercises.Count > 0)
                            {
                                hasExercises = true;
                                break;
                            }
                        }
                        catch { }
                    }

                    MainThread.BeginInvokeOnMainThread(() => dayItem.HasWorkout = hasExercises);
                })
                .ToList();

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Explicitly select a workout by ID.
        /// Used to ensure correct workout is displayed when multiple exist on same day.
        /// </summary>
        public void SelectWorkoutById(Guid workoutId)
        {
            _selectedWorkoutId = workoutId;
            _ = RefreshDailyWorkoutCardAsync();
        }
    }

    public class WeekDayItem : ObservableObject
    {
        private bool _isSelected;
        private bool _hasWorkout;

        public DateTime Date { get; set; }
        public string DayLabel { get; set; } = string.Empty;
        public string DayNumber { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool HasWorkout
        {
            get => _hasWorkout;
            set => SetProperty(ref _hasWorkout, value);
        }
    }
}
