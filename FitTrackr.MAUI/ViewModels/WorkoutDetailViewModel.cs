using CommunityToolkit.Mvvm.ComponentModel;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;

namespace FitTrackr.MAUI.ViewModels
{
    public partial class WorkoutDetailViewModel : ObservableObject
    {
        private readonly WorkoutService workoutService;

        [ObservableProperty]
        private WorkoutDto workout;

        public WorkoutDetailViewModel(WorkoutService workoutService)
        {
            this.workoutService = workoutService;
        }

        public async Task LoadWorkoutDetailsAsync(Guid workoutId)
        {
            Workout = await workoutService.GetWorkoutByIdAsync(workoutId);
        }
    }
}
