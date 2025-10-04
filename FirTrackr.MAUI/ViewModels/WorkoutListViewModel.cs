using CommunityToolkit.Mvvm.ComponentModel;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using System.Collections.ObjectModel;

namespace FitTrackr.MAUI.ViewModels
{
    public class WorkoutListViewModel : ObservableObject
    {
        private readonly WorkoutService _workoutService;

        public ObservableCollection<WorkoutSummaryDto> Workouts { get; set; } = new();

        public WorkoutListViewModel(WorkoutService service)
        {
            _workoutService = service;
        }

        public async Task LoadWorkoutsAsync()
        {
            if(Workouts.Any())
                return;

            var workouts = await _workoutService.GetWorkoutsAsync();

            foreach (var workout in workouts)
            {
                Workouts.Add(workout);
            }
        }
    }
}
