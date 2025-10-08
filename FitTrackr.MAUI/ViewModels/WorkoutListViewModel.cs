using CommunityToolkit.Mvvm.ComponentModel;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using System.Collections.ObjectModel;

namespace FitTrackr.MAUI.ViewModels
{
    public class WorkoutListViewModel : ObservableObject
    {
        private readonly WorkoutService _workoutService;

        public ObservableCollection<WorkoutSummaryDto> Workouts { get; } = new();
        public ObservableCollection<LocationDto> Locations { get; } = new();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public WorkoutListViewModel(WorkoutService workoutService)
        {
            _workoutService = workoutService;
        }

        public async Task LoadWorkoutsAsync()
        {
            if (Workouts.Any())
                return;

            try
            {
                IsLoading = true;
                var workouts = await _workoutService.GetWorkoutsAsync();

                Workouts.Clear();

                foreach (var workout in workouts)
                {
                    Workouts.Add(workout);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task AddWorkoutAsync(WorkoutRequestDto workout)
        {
            IsLoading = true;

            try
            {
                var addedWorkout = await _workoutService.AddWorkoutAsync(workout);

                Workouts.Add(addedWorkout);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task LoadLocationsAsync()
        {
            try
            {
                var locs = await _workoutService.GetLocationsAsync();
                Locations.Clear();

                foreach (var l in locs) Locations.Add(l);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
