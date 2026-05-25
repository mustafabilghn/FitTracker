using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;

namespace FitTrackr.API.Repositories
{
    public interface IWorkoutRepository
    {
        Task<List<Workout>> GetAllAsync(string userId);

        Task<Workout?> GetByIdAsync(Guid id);

        Task<Workout> CreateAsync(Workout workout,string userId);

        Task<Workout?> UpdateAsync(Guid id, Workout workout);

        Task<Workout?> DeleteAsync(Guid id);

        Task<DashboardSummaryDto> GetDashboardAsync(string userId);
    }
}
