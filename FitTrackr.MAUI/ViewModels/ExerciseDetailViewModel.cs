using CommunityToolkit.Mvvm.ComponentModel;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using System.Collections.ObjectModel;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class ExerciseDetailViewModel : ObservableObject
    {
        private readonly ExerciseService _exerciseService;
        private readonly ExerciseSetService _exerciseSetService;
        private Guid _exerciseId;

        [ObservableProperty]
        private ExerciseDto exercise = new();

        [ObservableProperty]
        private string newReps = string.Empty;

        [ObservableProperty]
        private double newWeightInKg;

        [ObservableProperty]
        private bool isBusy;

        public ExerciseDetailViewModel(ExerciseService exerciseService, ExerciseSetService exerciseSetService)
        {
            _exerciseService = exerciseService;
            _exerciseSetService = exerciseSetService;
        }

        public async Task LoadExerciseAsync(Guid exerciseId)
        {
            try
            {
                IsBusy = true;
                _exerciseId = exerciseId;

                Exercise = await _exerciseService.GetExerciseByIdAsync(exerciseId);
                Exercise.ExerciseSets = new ObservableCollection<ExerciseSetDto>((Exercise.ExerciseSets ?? []).OrderBy(x => x.SetNumber));
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task AddSetAsync()
        {
            if (_exerciseId == Guid.Empty)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(NewReps))
            {
                await Shell.Current.DisplayAlert("Hata", "Tekrar sayısı boş olamaz.", "Tamam");
                return;
            }

            var request = new ExerciseSetRequestDto
            {
                ExerciseId = _exerciseId,
                SetNumber = (Exercise.ExerciseSets?.Count ?? 0) + 1,
                Reps = NewReps.Trim(),
                WeightInKg = NewWeightInKg,
            };

            var addedSet = await _exerciseSetService.AddSetAsync(request);
            if (addedSet is null)
            {
                return;
            }

            NewReps = string.Empty;
            NewWeightInKg = 0;
            await LoadExerciseAsync(_exerciseId);
        }
        public async Task DeleteSetAsync(Guid setId)
        {
            if (_exerciseId == Guid.Empty)
            {
                return;
            }

            var deletedSet = await _exerciseSetService.DeleteSetAsync(setId);
            if (deletedSet is null)
            {
                return;
            }

            await LoadExerciseAsync(_exerciseId);
        }
    }
}
