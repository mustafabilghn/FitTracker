using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using System.Collections.ObjectModel;

namespace FitTrackr.MAUI.ViewModels
{
    public class AddWorkoutViewModel : ObservableObject
    {
        private readonly WorkoutService _workoutService;

        public ObservableCollection<LocationDto> Locations { get; set; } = new();

        public AddWorkoutViewModel(WorkoutService workoutService)
        {
            _workoutService = workoutService;
        }

        public async Task LoadLocationsAsync()
        {
            var locations = await _workoutService.GetLocationsAsync();
            Locations.Clear();

            foreach (var location in locations)
            {
                Locations.Add(location);
            }
        }

        public async Task<WorkoutSummaryDto> AddWorkoutAsync(WorkoutRequestDto workout)
        {
            var addedWorkout = await _workoutService.AddWorkoutAsync(workout);

            if (addedWorkout != null)
            {
                WeakReferenceMessenger.Default.Send(new WorkoutAddedMessage(addedWorkout));
            }

            return addedWorkout;
        }
    }
}
