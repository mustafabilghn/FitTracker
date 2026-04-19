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
        private readonly List<WorkoutSummaryDto> _allWorkouts = new();

        private DateTime _selectedDate;
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

            SelectedDate = DateTime.Today;
        }

        public async Task LoadWorkoutsAsync()
        {
            _allWorkouts.Clear();

            var workouts = await _workoutService.GetWorkoutsAsync();
            _allWorkouts.AddRange(workouts);

            BuildWeekStrip();
            await RefreshDailyWorkoutCardAsync();
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

            try
            {
                await _exerciseService.DeleteExerciseAsync(exercise.ExerciseId);
                card.RemoveExercise(exercise.ExerciseId);
            }
            catch (Exception ex)
            {
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
                    var updatedWorkout = await _workoutService.UpdateWorkoutAsync(card.WorkoutId.Value, new UpdateWorkoutRequestDto
                    {
                        WorkoutName = normalizedName,
                        WorkoutDate = card.WorkoutDate
                    });

                    SyncWorkout(updatedWorkout);
                    card.ApplyWorkout(updatedWorkout);
                    return;
                }

                var createdWorkout = await _workoutService.AddWorkoutAsync(new WorkoutRequestDto
                {
                    WorkoutName = normalizedName,
                    WorkoutDate = card.WorkoutDate,
                    DurationMinutes = 0,
                    LocationId = Guid.Empty
                });

                _allWorkouts.Add(createdWorkout);
                card.ApplyWorkout(createdWorkout);
                card.UpdateExerciseState(false);
                WeakReferenceMessenger.Default.Send(new WorkoutAddedMessage(createdWorkout));
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Hata", $"Antrenman adi guncellenemedi: {ex.Message}", "Tamam");
            }
        }

        private void SyncWorkout(WorkoutSummaryDto workout)
        {
            var existingWorkout = _allWorkouts.FirstOrDefault(w => w.Id == workout.Id);

            if (existingWorkout == null)
            {
                _allWorkouts.Add(workout);
                return;
            }

            existingWorkout.WorkoutName = workout.WorkoutName;
            existingWorkout.WorkoutDate = workout.WorkoutDate;
            existingWorkout.DurationMinutes = workout.DurationMinutes;
            existingWorkout.Location = workout.Location;
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
                    IsSelected = day.Date == SelectedDate.Date
                });
            }
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.Date.AddDays(-diff);
        }

        private async Task RefreshDailyWorkoutCardAsync()
        {
            DailyWorkouts.Clear();

            var selectedDate = SelectedDate.Date;
            var workoutForDay = _allWorkouts
                .Where(w => w.WorkoutDate.Date == selectedDate)
                .OrderBy(w => w.WorkoutDate)
                .FirstOrDefault();

            var card = new DailyWorkoutCardViewModel(selectedDate, workoutForDay);
            DailyWorkouts.Add(card);

            if (workoutForDay == null)
            {
                card.SetExercises([]);
                return;
            }

            try
            {
                var workoutDetail = await _workoutService.GetWorkoutByIdAsync(workoutForDay.Id);
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
            }
            catch
            {
                card.SetExercises([]);
            }
        }
    }

    public class WeekDayItem : ObservableObject
    {
        private bool _isSelected;

        public DateTime Date { get; set; }
        public string DayLabel { get; set; } = string.Empty;
        public string DayNumber { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
