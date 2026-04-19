using FitTrackr.API.Data;
using FitTrackr.API.Models.Domain;
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
                .ToListAsync();
        }

        public async Task<Workout?> GetByIdAsync(Guid id)
        {
            return await dbContext.Workouts
                .Include(e => e.Exercises)
                .ThenInclude(i => i.Intensity)
                .FirstOrDefaultAsync(x => x.Id == id);
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
