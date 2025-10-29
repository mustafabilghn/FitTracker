using FitTrackr.API.Models.Domain;

namespace FitTrackr.API.Repositories
{
    public interface IExerciseSetRepository
    {
        Task<ExerciseSet> CreateAsync(ExerciseSet exerciseSet);

        Task<List<ExerciseSet>> GetAllAsync();

        Task<ExerciseSet?> GetByIdAsync(Guid id);

        Task<ExerciseSet?> UpdateAsync(Guid id, ExerciseSet exerciseSet);

        Task<ExerciseSet?> DeleteAsync(Guid id);
    }
}
