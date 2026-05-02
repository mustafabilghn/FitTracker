using CommunityToolkit.Mvvm.ComponentModel;
using FitTrackr.MAUI.Models.DTO;
using FitTrackr.MAUI.Services;
using System.Collections.ObjectModel;

namespace FitTrackr.MAUI.ViewModels
{
    /// <summary>
    /// ViewModel for the Add Exercise quick entry flow.
    /// 
    /// NOTE: Does NOT send ExerciseAddedMessage anymore.
    /// Message is sent by ExerciseSetEntryViewModel as the single source,
    /// avoiding duplicate message broadcasts and race conditions.
    /// </summary>
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

        public async Task<ExerciseDto> AddExerciseAsync(ExerciseRequestDto exercise, DateTime workoutDate)
        {
            // Add exercise to backend
            // Message send is handled by ExerciseSetEntryViewModel (single source)
            return await _exerciseService.AddExerciseAsync(exercise);
        }
    }
}
