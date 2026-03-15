using FitTrackr.API.Models.Domain;

namespace FitTrackr.API.Repositories
{
    public interface IWorkoutRepository
    {
        Task<List<Workout>> GetAllAsync(string userId);

        Task<Workout?> GetByIdAsync(Guid id);

        Task<Workout> CreateAsync(Workout workout,string userId);

        Task<Workout?> UpdateAsync(Guid id, Workout workout);

        Task<Workout?> DeleteAsync(Guid id);
    }
}
