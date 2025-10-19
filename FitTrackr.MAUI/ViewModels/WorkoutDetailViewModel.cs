using CommunityToolkit.Mvvm.ComponentModel;
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

        [ObservableProperty]
        private WorkoutDto workout;

        public ObservableCollection<ExerciseDto> Exercises { get; } = new();

        public WorkoutDetailViewModel(WorkoutService workoutService)
        {
            this.workoutService = workoutService;

            WeakReferenceMessenger.Default.Register<ExerciseAddedMessage>(this, async (r, m) =>
            {
                if(Workout != null && Workout.Id == m.Value)
                {
                    await LoadWorkoutDetailsAsync(Workout.Id);
                }
            });
        }

        public async Task LoadWorkoutDetailsAsync(Guid workoutId)
        {
            Workout = await workoutService.GetWorkoutByIdAsync(workoutId);
        }
    }
}
