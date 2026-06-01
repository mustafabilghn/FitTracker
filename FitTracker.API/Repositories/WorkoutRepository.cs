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

            await dbContext.Workouts.AddAsync(workout);
            await dbContext.SaveChangesAsync();
            return workout;
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
            return await dbContext.Workouts
                .Where(w => w.userId == userId)
                .Include(w => w.Exercises)
                .ToListAsync();
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
            var workouts = await dbContext.Workouts
                .Where(w => w.userId == userId)
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
