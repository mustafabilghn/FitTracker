using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace FitTrackr.MAUI.ViewModels
{
    public class WorkoutListViewModel : ObservableObject
    {
        private readonly WorkoutService _workoutService;
        public ObservableCollection<WorkoutSummaryDto> Workouts { get; set; } = new();
        public ICommand DeleteCommand { get; }
        public ICommand WorkoutSelectedCommand { get; }

        public WorkoutListViewModel(WorkoutService service)
        {
            _workoutService = service;

            DeleteCommand = new AsyncRelayCommand<Guid>(DeleteWorkoutAsync);
            WorkoutSelectedCommand = new RelayCommand<WorkoutSummaryDto>(OnWorkoutSelectedAsync);

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

        private async Task DeleteWorkoutAsync(Guid id)
        {
            try
            {
                var deletedWorkout = await _workoutService.DeleteWorkoutAsync(id);

                var workoutToRemove = Workouts.FirstOrDefault(w => w.Id == id);

                if (workoutToRemove != null)
                {
                    Workouts.Remove(workoutToRemove);

                    WeakReferenceMessenger.Default.Send(new WorkoutDeletedMessage(id));
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Hata", $"Silme işlemi başarısız: {ex.Message}", "Tamam");
            }
        }

        private void OnWorkoutSelectedAsync(WorkoutSummaryDto selectedWorkout)
        {
            if (selectedWorkout == null)
                return;

            WeakReferenceMessenger.Default.Send(new WorkoutSelectedMessage(selectedWorkout.Id));
        }
    }
}
