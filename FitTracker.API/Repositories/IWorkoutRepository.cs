using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;

namespace FitTrackr.API.Repositories
{
    public enum CompleteWorkoutResult
    {
        /// <summary>Workout yok veya current user'a ait değil (varlığı sızdırılmaz).</summary>
        NotFound,
        /// <summary>Planned → Completed geçişi yapıldı.</summary>
        Completed,
        /// <summary>Zaten Completed; değişiklik yapılmadı (idempotent).</summary>
        AlreadyCompleted
    }

    public interface IWorkoutRepository
    {
        /// <summary>Kullanıcının YAPTIĞI (Completed) antrenmanlar — geçmiş/ilerleme listesi.</summary>
        Task<List<Workout>> GetAllAsync(string userId);

        /// <summary>Kullanıcının kayıtlı (Planned) planları, tarihe göre artan sırada; egzersiz ve setleriyle.</summary>
        Task<List<Workout>> GetPlannedAsync(string userId);

        /// <summary>
        /// Kullanıcıya ait bir Planned workout'u Completed yapar. Yalnızca Planned → Completed geçişi vardır.
        /// Başka kullanıcının workout'u için <see cref="CompleteWorkoutResult.NotFound"/> döner ve hiçbir şey değişmez.
        /// </summary>
        Task<(CompleteWorkoutResult Result, Workout? Workout)> CompletePlannedAsync(Guid id, string userId);

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
