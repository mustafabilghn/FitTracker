using FitTrackr.API.Data;
using FitTrackr.API.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitTrackr.API.Repositories
{
    public class ExerciseRepository : IExerciseRepository
    {
        private readonly FitTrackrDbContext dbContext;

        public ExerciseRepository(FitTrackrDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<Exercise> CreateAsync(Exercise exercise)
        {
            var intensity = await dbContext.Intensities.FirstOrDefaultAsync(i => i.Id == exercise.IntensityId);

            if (intensity is null)
            {
                throw new Exception("Invalid IntensityId provided");
            }

            exercise.Intensity = intensity;

            await dbContext.Exercises.AddAsync(exercise);

            await dbContext.SaveChangesAsync();

            return exercise;
        }

        public async Task<Exercise?> DeleteAsync(Guid id)
        {
            var exercise = await dbContext.Exercises.Include(i => i.Intensity).FirstOrDefaultAsync(x => x.Id == id);

            if (exercise is null)
            {
                return null;
            }

            var intensity = await dbContext.Intensities.FirstOrDefaultAsync(i => i.Id == exercise.IntensityId);

            if (intensity is null)
            {
                throw new Exception("Invalid IntensityId provided");
            }

            exercise.Intensity = intensity;

            dbContext.Remove(exercise);

            await dbContext.SaveChangesAsync();

            return exercise;
        }

        public async Task<List<Exercise>> GetAllAsync()
        {
            return await dbContext.Exercises.Include("Intensity").Include("Workout").ToListAsync();
        }

        public async Task<Exercise?> GetByIdAsync(Guid id)
        {
            return await dbContext.Exercises.Include("Intensity").Include("Workout").FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<Exercise?> UpdateAsync(Guid id, Exercise exercise)
        {
            var existingExercise = await dbContext.Exercises.Include(i => i.Intensity).FirstOrDefaultAsync(x => x.Id == id);

            if (existingExercise == null)
            {
                return null;
            }

            var intensity = await dbContext.Intensities.FirstOrDefaultAsync(i => i.Id == exercise.IntensityId);

            if (intensity is null)
            {
                throw new Exception("Invalid IntensityId provided");
            }

            existingExercise.Intensity = intensity;

            existingExercise.ExerciseName = exercise.ExerciseName;
            existingExercise.Sets = exercise.Sets;
            existingExercise.Reps = exercise.Reps;
            existingExercise.WeightInKg = exercise.WeightInKg;

            await dbContext.SaveChangesAsync();

            return existingExercise;
        }
    }
}
