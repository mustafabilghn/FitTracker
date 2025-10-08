using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
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

            WeakReferenceMessenger.Default.Register<WorkoutAddedMessage>(this, (r, m) =>
            {
                Workouts.Add(m.Value);
            });
        }

        public async Task LoadWorkoutsAsync()
        {
            if (Workouts.Any())
                return;

            var workouts = await _workoutService.GetWorkoutsAsync();

            foreach (var workout in workouts)
            {
                Workouts.Add(workout);
            }
        }
    }
}
