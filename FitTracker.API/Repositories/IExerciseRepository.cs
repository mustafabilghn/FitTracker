using FitTrackr.API.Models.Domain;

namespace FitTrackr.API.Repositories
{
    public interface IExerciseRepository
    {
        Task<Exercise> CreateAsync(Exercise exercise);

        Task<List<Exercise>> GetAllAsync();

        Task<Exercise?> GetByIdAsync(Guid id);

        Task<Exercise?> UpdateAsync(Guid id, Exercise exercise);

        Task<Exercise?> DeleteAsync(Guid id);
    }
}
