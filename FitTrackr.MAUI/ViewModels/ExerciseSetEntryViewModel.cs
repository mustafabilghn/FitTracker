using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using System.Collections.ObjectModel;
using System.Globalization;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class ExerciseSetEntryViewModel : ObservableObject
    {
        private readonly ExerciseService _exerciseService;
        private readonly ExerciseSetService _exerciseSetService;
        private readonly WorkoutService _workoutService;

        private ExerciseCatalogItemDto? _selectedExercise;
        private Guid? _workoutId;
        private DateTime _workoutDate;
        private string _workoutName = DailyWorkoutCardViewModel.DefaultWorkoutName;

        private bool _isEditMode;
        private Guid _exerciseId;
        private readonly List<Guid> _originalSetIds = new();

        [ObservableProperty]
        private string exerciseName = string.Empty;

        [ObservableProperty]
        private string repsInput = string.Empty;

        [ObservableProperty]
        private string weightInput = string.Empty;

        [ObservableProperty]
        private bool isBusy;

        public ObservableCollection<PendingExerciseSetItem> PendingSets { get; } = new();

        public bool HasPendingSets => PendingSets.Count > 0;

        public ExerciseSetEntryViewModel(
            ExerciseService exerciseService,
            ExerciseSetService exerciseSetService,
            WorkoutService workoutService)
        {
            _exerciseService = exerciseService;
            _exerciseSetService = exerciseSetService;
            _workoutService = workoutService;
        }

        public void Initialize(ExerciseCatalogItemDto selectedExercise, Guid? workoutId, DateTime workoutDate, string workoutName)
        {
            _isEditMode = false;
            _exerciseId = Guid.Empty;
            _originalSetIds.Clear();

            _selectedExercise = selectedExercise;
            _workoutId = workoutId;
            _workoutDate = workoutDate.Date;
            _workoutName = string.IsNullOrWhiteSpace(workoutName)
                ? DailyWorkoutCardViewModel.DefaultWorkoutName
                : workoutName.Trim();

            ExerciseName = selectedExercise.Name;
            RepsInput = string.Empty;
            WeightInput = string.Empty;
            PendingSets.Clear();
            OnPropertyChanged(nameof(HasPendingSets));
        }

        public void InitializeForExistingExercise(Guid exerciseId, string exerciseName)
        {
            _isEditMode = true;
            _exerciseId = exerciseId;
            _selectedExercise = null;
            _originalSetIds.Clear();

            ExerciseName = string.IsNullOrWhiteSpace(exerciseName) ? "Egzersiz" : exerciseName.Trim();
            RepsInput = string.Empty;
            WeightInput = string.Empty;
            PendingSets.Clear();
            OnPropertyChanged(nameof(HasPendingSets));

            _ = LoadExistingSetsAsync();
        }

        [RelayCommand]
        private async Task AddSetAsync()
        {
            if (string.IsNullOrWhiteSpace(RepsInput))
            {
                await Shell.Current.DisplayAlert("Hata", "Tekrar sayisi bos olamaz.", "Tamam");
                return;
            }

            if (!TryParseWeight(WeightInput, out var weightInKg))
            {
                await Shell.Current.DisplayAlert("Hata", "Agirlik degeri gecerli degil.", "Tamam");
                return;
            }

            PendingSets.Add(new PendingExerciseSetItem
            {
                Id = null,
                SetNumber = PendingSets.Count + 1,
                Reps = RepsInput.Trim(),
                WeightInKg = weightInKg
            });

            RepsInput = string.Empty;
            WeightInput = string.Empty;
            OnPropertyChanged(nameof(HasPendingSets));
        }

        [RelayCommand]
        private async Task EditSetAsync(PendingExerciseSetItem? pendingSet)
        {
            if (pendingSet == null)
            {
                return;
            }

            var updatedReps = await Shell.Current.DisplayPromptAsync(
                "Set Düzenle",
                "Tekrar sayısı",
                accept: "Kaydet",
                cancel: "İptal",
                initialValue: pendingSet.Reps,
                keyboard: Keyboard.Numeric);

            if (updatedReps == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(updatedReps))
            {
                await Shell.Current.DisplayAlert("Hata", "Tekrar sayisi bos olamaz.", "Tamam");
                return;
            }

            var updatedWeight = await Shell.Current.DisplayPromptAsync(
                "Set Düzenle",
                "Ağırlık (kg)",
                accept: "Kaydet",
                cancel: "İptal",
                initialValue: pendingSet.WeightInKg.ToString("0.##", CultureInfo.CurrentCulture),
                keyboard: Keyboard.Numeric);

            if (updatedWeight == null)
            {
                return;
            }

            if (!TryParseWeight(updatedWeight, out var weightInKg))
            {
                await Shell.Current.DisplayAlert("Hata", "Agirlik degeri gecerli degil.", "Tamam");
                return;
            }

            pendingSet.Reps = updatedReps.Trim();
            pendingSet.WeightInKg = weightInKg;
        }

        [RelayCommand]
        private void RemoveSet(PendingExerciseSetItem? pendingSet)
        {
            if (pendingSet == null)
            {
                return;
            }

            if (!PendingSets.Remove(pendingSet))
            {
                return;
            }

            ReindexSetNumbers();
            OnPropertyChanged(nameof(HasPendingSets));
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            if (!_isEditMode && _selectedExercise == null)
            {
                return;
            }

            if (PendingSets.Count == 0)
            {
                await Shell.Current.DisplayAlert("Hata", "Lutfen en az bir set ekleyin.", "Tamam");
                return;
            }

            try
            {
                IsBusy = true;

                if (_isEditMode)
                {
                    await SaveExistingExerciseAsync();
                    await Shell.Current.Navigation.PopAsync();
                    return;
                }

                await SaveNewExerciseAsync();
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Hata", $"Egzersiz kaydedilemedi: {ex.Message}", "Tamam");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveNewExerciseAsync()
        {
            var resolvedWorkoutId = await EnsureWorkoutIdAsync();
            if (resolvedWorkoutId == Guid.Empty)
            {
                return;
            }

            var intensityId = await ResolveDefaultIntensityIdAsync();
            if (intensityId == Guid.Empty)
            {
                return;
            }

            var addedExercise = await _exerciseService.AddExerciseAsync(new ExerciseRequestDto
            {
                ExerciseName = _selectedExercise!.Name,
                WorkoutId = resolvedWorkoutId,
                IntensityId = intensityId
            });

            for (var i = 0; i < PendingSets.Count; i++)
            {
                var set = PendingSets[i];

                await _exerciseSetService.AddSetAsync(new ExerciseSetRequestDto
                {
                    ExerciseId = addedExercise.Id,
                    SetNumber = i + 1,
                    Reps = set.Reps,
                    WeightInKg = set.WeightInKg
                });
            }

            WeakReferenceMessenger.Default.Send(new ExerciseAddedMessage(resolvedWorkoutId));
            await Shell.Current.Navigation.PopToRootAsync();
        }

        private async Task SaveExistingExerciseAsync()
        {
            ReindexSetNumbers();

            var currentExistingSetIds = PendingSets
                .Where(x => x.Id.HasValue)
                .Select(x => x.Id!.Value)
                .ToHashSet();

            foreach (var removedSetId in _originalSetIds.Where(id => !currentExistingSetIds.Contains(id)))
            {
                await _exerciseSetService.DeleteSetAsync(removedSetId);
            }

            for (var i = 0; i < PendingSets.Count; i++)
            {
                var set = PendingSets[i];

                if (set.Id.HasValue)
                {
                    await _exerciseSetService.UpdateSetAsync(set.Id.Value, new UpdateExerciseSetRequestDto
                    {
                        SetNumber = i + 1,
                        Reps = set.Reps,
                        WeightInKg = set.WeightInKg
                    });
                    continue;
                }

                await _exerciseSetService.AddSetAsync(new ExerciseSetRequestDto
                {
                    ExerciseId = _exerciseId,
                    SetNumber = i + 1,
                    Reps = set.Reps,
                    WeightInKg = set.WeightInKg
                });
            }
        }

        private async Task LoadExistingSetsAsync()
        {
            if (_exerciseId == Guid.Empty)
            {
                return;
            }

            try
            {
                IsBusy = true;
                var exercise = await _exerciseService.GetExerciseByIdAsync(_exerciseId);

                PendingSets.Clear();
                _originalSetIds.Clear();

                foreach (var set in (exercise.ExerciseSets ?? []).OrderBy(x => x.SetNumber))
                {
                    PendingSets.Add(new PendingExerciseSetItem
                    {
                        Id = set.Id,
                        SetNumber = set.SetNumber,
                        Reps = set.Reps,
                        WeightInKg = set.WeightInKg
                    });

                    _originalSetIds.Add(set.Id);
                }

                OnPropertyChanged(nameof(HasPendingSets));
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Hata", $"Setler yuklenemedi: {ex.Message}", "Tamam");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ReindexSetNumbers()
        {
            for (var i = 0; i < PendingSets.Count; i++)
            {
                PendingSets[i].SetNumber = i + 1;
            }
        }

        private async Task<Guid> EnsureWorkoutIdAsync()
        {
            if (_workoutId.HasValue && _workoutId.Value != Guid.Empty)
            {
                return _workoutId.Value;
            }

            var createdWorkout = await _workoutService.AddWorkoutAsync(new WorkoutRequestDto
            {
                WorkoutName = _workoutName,
                WorkoutDate = _workoutDate,
                DurationMinutes = 0,
                LocationId = Guid.Empty
            });

            _workoutId = createdWorkout.Id;
            return createdWorkout.Id;
        }

        private async Task<Guid> ResolveDefaultIntensityIdAsync()
        {
            var intensities = await _exerciseService.GetIntensitiesAsync();
            var defaultIntensity = intensities.FirstOrDefault();

            if (defaultIntensity == null)
            {
                await Shell.Current.DisplayAlert("Hata", "Yogunluk verisi alinamadi.", "Tamam");
                return Guid.Empty;
            }

            return defaultIntensity.Id;
        }

        private static bool TryParseWeight(string input, out double result)
        {
            var normalized = (input ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                result = 0;
                return true;
            }

            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out result)
                || double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }

    public class PendingExerciseSetItem : ObservableObject
    {
        private int _setNumber;
        private string _reps = string.Empty;
        private double _weightInKg;

        public Guid? Id { get; set; }

        public int SetNumber
        {
            get => _setNumber;
            set => SetProperty(ref _setNumber, value);
        }

        public string Reps
        {
            get => _reps;
            set
            {
                if (SetProperty(ref _reps, value))
                {
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        public double WeightInKg
        {
            get => _weightInKg;
            set
            {
                if (SetProperty(ref _weightInKg, value))
                {
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        public string DisplayText => $"{Reps} x {WeightInKg:0.##} kg";
    }
}
