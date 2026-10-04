using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;

namespace FitTrackr.API.Repositories
{
    public interface IWorkoutRepository
    {
        Task<List<Workout>> GetAllAsync(string userId);

        Task<Workout?> GetByIdAsync(Guid id);

        Task<Workout> CreateAsync(Workout workout,string userId);

        /// <summary>
        /// Workout → Exercise → ExerciseSet grafiğini tek bir transaction içinde, atomik olarak oluşturur.
        /// Tüm egzersizler seed edilmiş <paramref name="intensityLevel"/> referans kaydına bağlanır; seviye bulunamazsa
        /// hiçbir şey yazılmaz ve null döner. Hata olursa transaction geri alınır ve graf change tracker'dan çıkarılır.
        /// </summary>
        Task<Workout?> CreateWithExercisesAsync(Workout workout, string userId, string intensityLevel, CancellationToken cancellationToken = default);

        Task<Workout?> UpdateAsync(Guid id, Workout workout);

        Task<Workout?> DeleteAsync(Guid id);

        Task<DashboardSummaryDto> GetDashboardAsync(string userId);
    }
}
