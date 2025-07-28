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

        public async Task<Workout> CreateAsync(Workout workout)
        {
            var location = await dbContext.Locations.FirstOrDefaultAsync(w => w.Id == workout.LocationId);

            if (location is null)
            {
                return null;
            }

            workout.Location = location;

            await dbContext.Workouts.AddAsync(workout);

            await dbContext.SaveChangesAsync();

            return workout;
        }

        public async Task<Workout?> DeleteAsync(Guid id)
        {
            var workout = await dbContext.Workouts.Include(w => w.Location).Include(e => e.Exercises).ThenInclude(i => i.Intensity).FirstOrDefaultAsync(w => w.Id == id);

            if (workout is null)
            {
                return null;
            }

            dbContext.Workouts.Remove(workout);

            await dbContext.SaveChangesAsync();

            return workout;
        }

        public async Task<List<Workout>> GetAllAsync()
        {
            return await dbContext.Workouts.Include(l => l.Location).ToListAsync();
        }

        public async Task<Workout?> GetByIdAsync(Guid id)
        {
            return await dbContext.Workouts.Include(l => l.Location).Include(e => e.Exercises).ThenInclude(i => i.Intensity).FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<Workout?> UpdateAsync(Guid id, Workout workout)
        {
            var existingWorkout = await dbContext.Workouts.Include(l => l.Location).FirstOrDefaultAsync(w => w.Id == id);

            if (existingWorkout is null)
            {
                return null;
            }

            var location = await dbContext.Locations.FirstOrDefaultAsync(w => w.Id == workout.LocationId);

            if (location is null)
            {
                return null;
            }

            existingWorkout.Location = location;
            existingWorkout.WorkoutName = workout.WorkoutName;
            existingWorkout.WorkoutDate = workout.WorkoutDate;
            existingWorkout.DurationMinutes = workout.DurationMinutes;

            await dbContext.SaveChangesAsync();

            return existingWorkout;
        }
    }
}
