using FitTrackr.API.Models.Domain;

namespace FitTrackr.API.Repositories
{
    public interface IWorkoutRepository
    {
        Task<List<Workout>> GetAllAsync();

        Task<Workout?> GetByIdAsync(Guid id);

        Task<Workout> CreateAsync(Workout workout);

        Task<Workout?> UpdateAsync(Guid id, Workout workout);

        Task<Workout?> DeleteAsync(Guid id);
    }
}
