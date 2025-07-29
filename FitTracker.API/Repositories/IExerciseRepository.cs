using FitTrackr.API.Models.Domain;

namespace FitTrackr.API.Repositories
{
    public interface IExerciseRepository
    {
        Task<Exercise> CreateAsync(Exercise exercise);

        Task<List<Exercise>> GetAllAsync(string? filterOn = null, string? filterQuery = null, string? sortBy = null, bool isAscending = true, int pageNumber = 1, int pageSize = 1000);

        Task<Exercise?> GetByIdAsync(Guid id);

        Task<Exercise?> UpdateAsync(Guid id, Exercise exercise);

        Task<Exercise?> DeleteAsync(Guid id);
    }
}
