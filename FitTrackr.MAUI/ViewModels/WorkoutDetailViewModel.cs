using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using System.Collections.ObjectModel;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class WorkoutDetailViewModel : ObservableObject
    {
        private readonly WorkoutService workoutService;
        private readonly ExerciseService exerciseService;
        private readonly ExerciseSetService exerciseSetService;

        [ObservableProperty]
        private WorkoutDto workout;

        [ObservableProperty]
        private string newReps = string.Empty;

        [ObservableProperty]
        private double newWeightInKg;

        public ObservableCollection<ExerciseDto> Exercises { get; } = new();

        public WorkoutDetailViewModel(WorkoutService workoutService, ExerciseService exerciseService, ExerciseSetService exerciseSetService)
        {
            this.workoutService = workoutService;
            this.exerciseService = exerciseService;
            this.exerciseSetService = exerciseSetService;

            WeakReferenceMessenger.Default.Register<ExerciseAddedMessage>(this, async (r, m) =>
            {
                if (Workout != null && Workout.Id == m.Value)
                {
                    await LoadWorkoutDetailsAsync(Workout.Id);
                }
            });
        }

        public async Task LoadWorkoutDetailsAsync(Guid workoutId)
        {
            Workout = await workoutService.GetWorkoutByIdAsync(workoutId);

            Exercises.Clear();

            if (Workout.Exercises != null)
            {
                foreach (var exercise in Workout.Exercises)
                {
                    var exerciseWithSets = await exerciseService.GetExerciseByIdAsync(exercise.Id);
                    exerciseWithSets.ExerciseSets = exerciseWithSets.ExerciseSets?
                        .OrderBy(s => s.SetNumber)
                        .ToList();

                    Exercises.Add(exerciseWithSets);
                }
            }
        }

        [RelayCommand]
        public async Task DeleteExerciseAsync(Guid id)
        {
            var exercise = await exerciseService.DeleteExerciseAsync(id);
            if (exercise != null)
            {
                var exerciseToRemove = Exercises.FirstOrDefault(x => x.Id == id);
                if (exerciseToRemove != null)
                {
                    Exercises.Remove(exerciseToRemove);
                }
            }
        }

        [RelayCommand]
        public void ToggleExercise(ExerciseDto exercise)
        {
            if (exercise is null)
            {
                return;
            }

            exercise.IsExpanded = !exercise.IsExpanded;
        }

        [RelayCommand]
        public void ToggleAddSet(ExerciseDto exercise)
        {
            exercise.IsAddingSet = !exercise.IsAddingSet;
        }

        [RelayCommand]
        public async Task AddSetAsync(ExerciseDto exercise)
        {
            var request = new ExerciseSetRequestDto
            {
                ExerciseId = exercise.Id,
                SetNumber = (exercise.ExerciseSets?.Count ?? 0) + 1,
                Reps = NewReps,
                WeightInKg = NewWeightInKg,
            };

            await exerciseSetService.AddSetAsync(request);

            NewReps = string.Empty;
            NewWeightInKg = 0;
            exercise.IsAddingSet = false;

            await LoadWorkoutDetailsAsync(Workout.Id);
        }
    }
}
