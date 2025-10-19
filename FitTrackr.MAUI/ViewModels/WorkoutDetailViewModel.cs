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

        [ObservableProperty]
        private WorkoutDto workout;

        public ObservableCollection<ExerciseDto> Exercises { get; } = new();

        public WorkoutDetailViewModel(WorkoutService workoutService, ExerciseService exerciseService)
        {
            this.workoutService = workoutService;
            this.exerciseService = exerciseService;

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
                    Exercises.Add(exercise);
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
    }
}
