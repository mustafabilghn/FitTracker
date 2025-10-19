using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FitTrackr.MAUI.Messages;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using System.Collections.ObjectModel;

namespace FitTrackr.MAUI.ViewModels
{
    public class AddExerciseViewModel : ObservableObject
    {
        private readonly ExerciseService _exerciseService;

        public ObservableCollection<IntensityDto> Intensities { get; set; } = new();

        public AddExerciseViewModel(ExerciseService exerciseService)
        {
            _exerciseService = exerciseService;
        }

        public async Task LoadIntensitiesAsync()
        {
            var intensities = await _exerciseService.GetIntensitiesAsync();
            Intensities.Clear();

            foreach (var intensity in intensities)
            {
                Intensities.Add(intensity);
            }
        }

        public async Task<ExerciseDto> AddExerciseAsync(ExerciseRequestDto exercise)
        {
            var addedExercise = await _exerciseService.AddExerciseAsync(exercise);

            if (addedExercise != null)
            {
                WeakReferenceMessenger.Default.Send(new ExerciseAddedMessage(exercise.WorkoutId));
            }

            return addedExercise;
        }
    }
}
