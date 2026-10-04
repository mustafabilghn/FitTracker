using FitTrackr.API.Data;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using Microsoft.EntityFrameworkCore;

namespace FitTrackr.API.Repositories
{
    public class WorkoutRepository : IWorkoutRepository
    {
        private readonly FitTrackrDbContext dbContext;

        public WorkoutRepository(FitTrackrDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<Workout> CreateAsync(Workout workout, string userId)
        {
            workout.userId = userId;
            // Kullanıcının kendi kaydettiği antrenman her zaman "yapılmış" antrenmandır (POST /api/Workout).
            workout.Status = WorkoutStatus.Completed;

            await dbContext.Workouts.AddAsync(workout);
            await dbContext.SaveChangesAsync();
            return workout;
        }

        public async Task<Workout?> CreateWithExercisesAsync(Workout workout, string userId, string intensityLevel, CancellationToken cancellationToken = default)
        {
            // Referans veri (seed) doğal anahtarıyla çözülür; uydurma/sabit GUID kullanılmaz.
            var intensity = await dbContext.Intensities
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Level == intensityLevel, cancellationToken);

            if (intensity is null)
                return null;

            workout.userId = userId;
            foreach (var exercise in workout.Exercises ?? Enumerable.Empty<Exercise>())
                exercise.IntensityId = intensity.Id;

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await dbContext.Workouts.AddAsync(workout, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return workout;
            }
            catch
            {
                // Transaction dispose edilince geri alınır. Başarısız grafı change tracker'dan da çıkar ki aynı
                // scope'taki sonraki bir SaveChanges onu tekrar (kısmen) yazmaya çalışmasın.
                DetachGraph(workout);
                throw;
            }
        }

        private void DetachGraph(Workout workout)
        {
            // Önce grafın anlık kopyası alınır: detach sırasında EF Core navigation fixup'ı koleksiyonları değiştirebilir;
            // canlı koleksiyon üzerinde dolaşmak yarıda kesilip grafın bir kısmını Added bırakırdı.
            var exercises = (workout.Exercises ?? Enumerable.Empty<Exercise>()).ToList();
            var graph = new List<object> { workout };
            graph.AddRange(exercises);
            graph.AddRange(exercises.SelectMany(e => e.ExerciseSets ?? Enumerable.Empty<ExerciseSet>()).ToList());

            foreach (var entity in graph)
            {
                var entry = dbContext.Entry(entity);
                if (entry.State != EntityState.Detached)
                    entry.State = EntityState.Detached;
            }
        }

        public async Task<Workout?> DeleteAsync(Guid id)
        {
            var workout = await dbContext.Workouts
                .Include(e => e.Exercises)
                .ThenInclude(i => i.Intensity)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (workout is null)
            {
                return null;
            }

            dbContext.Workouts.Remove(workout);

            await dbContext.SaveChangesAsync();

            return workout;
        }

        public async Task<List<Workout>> GetAllAsync(string userId)
        {
            // Geçmiş/ilerleme listesi: yalnızca yapılmış antrenmanlar. Mevcut istemciler (MAUI ilerleme ekranı) bu listeden
            // ağırlık/hacim/1RM hesaplar; planlar burada olsaydı yapılmış gibi görünürdü. Planlar: GetPlannedAsync.
            return await dbContext.Workouts
                .Where(w => w.userId == userId && w.Status == WorkoutStatus.Completed)
                .Include(w => w.Exercises)
                .ToListAsync();
        }

        public async Task<List<Workout>> GetPlannedAsync(string userId)
        {
            return await dbContext.Workouts
                .AsNoTracking()
                .Where(w => w.userId == userId && w.Status == WorkoutStatus.Planned)
                .Include(w => w.Exercises)
                    .ThenInclude(e => e.ExerciseSets)
                .OrderBy(w => w.WorkoutDate)
                .ToListAsync();
        }

        public async Task<(CompleteWorkoutResult Result, Workout? Workout)> CompletePlannedAsync(Guid id, string userId)
        {
            // Sahiplik sorgunun parçasıdır: başka kullanıcının workout'u hiç yüklenmez, "bulunamadı" ile aynı sonuç döner.
            var workout = await dbContext.Workouts
                .Include(w => w.Exercises)
                .FirstOrDefaultAsync(w => w.Id == id && w.userId == userId);

            if (workout is null)
                return (CompleteWorkoutResult.NotFound, null);

            if (workout.Status == WorkoutStatus.Completed)
                return (CompleteWorkoutResult.AlreadyCompleted, workout);

            workout.Status = WorkoutStatus.Completed;

            // İleri tarihli bir plan bugün tamamlanıyorsa tarih bugüne çekilir: aksi halde "yapılmış" bir antrenman gelecekte
            // görünür ve analizdeki gün hesapları (son antrenmandan bu yana geçen gün vb.) negatife düşerdi.
            var today = DateTime.UtcNow.Date;
            if (workout.WorkoutDate.Date > today)
                workout.WorkoutDate = today;

            await dbContext.SaveChangesAsync();
            return (CompleteWorkoutResult.Completed, workout);
        }

        public async Task<Workout?> GetByIdAsync(Guid id)
        {
            return await dbContext.Workouts
                .Include(e => e.Exercises)
                .ThenInclude(i => i.Intensity)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<DashboardSummaryDto> GetDashboardAsync(string userId)
        {
            // Dashboard (aktif günler / PR'lar): yalnızca yapılmış antrenmanlar.
            var workouts = await dbContext.Workouts
                .Where(w => w.userId == userId && w.Status == WorkoutStatus.Completed)
                .Include(w => w.Exercises)
                    .ThenInclude(e => e.ExerciseSets)
                .AsNoTracking()
                .ToListAsync();

            var activeDates = new HashSet<DateTime>();
            double maxBP = 0, maxSQ = 0, maxDL = 0, maxBR = 0, maxOP = 0;

            foreach (var workout in workouts)
            {
                foreach (var exercise in workout.Exercises ?? Enumerable.Empty<Exercise>())
                {
                    if (exercise?.ExerciseSets == null || exercise.ExerciseSets.Count == 0)
                        continue;

                    activeDates.Add(workout.WorkoutDate.Date);

                    var maxWeight = exercise.ExerciseSets.Max(s => s.WeightInKg);
                    var name = exercise.ExerciseName ?? string.Empty;

                    if (name.Equals("Bench Press", StringComparison.OrdinalIgnoreCase))
                        maxBP = Math.Max(maxBP, maxWeight);
                    else if (name.Equals("Squat", StringComparison.OrdinalIgnoreCase))
                        maxSQ = Math.Max(maxSQ, maxWeight);
                    else if (name.Equals("Deadlift", StringComparison.OrdinalIgnoreCase))
                        maxDL = Math.Max(maxDL, maxWeight);
                    else if (name.Equals("Barbell Row", StringComparison.OrdinalIgnoreCase))
                        maxBR = Math.Max(maxBR, maxWeight);
                    else if (name.Equals("Overhead Press", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("Barbell Overhead Press", StringComparison.OrdinalIgnoreCase))
                        maxOP = Math.Max(maxOP, maxWeight);
                }
            }

            return new DashboardSummaryDto
            {
                ActiveDates = activeDates.OrderBy(d => d).ToList(),
                BenchPressMaxKg = maxBP,
                SquatMaxKg = maxSQ,
                DeadliftMaxKg = maxDL,
                BarbellRowMaxKg = maxBR,
                OhpMaxKg = maxOP
            };
        }

        public async Task<Workout?> UpdateAsync(Guid id, Workout workout)
        {
            var existingWorkout = await dbContext.Workouts.FirstOrDefaultAsync(w => w.Id == id);

            if (existingWorkout is null)
            {
                return null;
            }

            existingWorkout.WorkoutName = workout.WorkoutName;
            existingWorkout.WorkoutDate = workout.WorkoutDate;

            await dbContext.SaveChangesAsync();

            return existingWorkout;
        }
    }
}
