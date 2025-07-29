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
            var intensity = await dbContext.Intensities.FindAsync(exercise.IntensityId);

            if (intensity is null)
            {
                return null;
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

            dbContext.Remove(exercise);

            await dbContext.SaveChangesAsync();

            return exercise;
        }

        public async Task<List<Exercise>> GetAllAsync(string? filterOn = null, string? filterQuery = null, string? sortBy = null, bool isAscending = true, int pageNumber = 1, int pageSize = 1000)
        {
            var exercise = dbContext.Exercises.Include(e => e.Intensity).Include(e => e.Workout).AsQueryable();

            //Filtering
            if (string.IsNullOrWhiteSpace(filterOn) == false && string.IsNullOrWhiteSpace(filterQuery) == false)
            {
                if (filterOn.Equals("ExerciseName", StringComparison.OrdinalIgnoreCase) ||
                    filterOn.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                    filterOn.Equals("Exercise", StringComparison.OrdinalIgnoreCase))
                {
                    exercise = exercise.Where(e => e.ExerciseName.Contains(filterQuery));
                }
            }

            //Sorting
            if (string.IsNullOrWhiteSpace(sortBy) == false)
            {
                if (sortBy.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    exercise = isAscending ? exercise.OrderBy(e => e.ExerciseName) : exercise.OrderByDescending(e => e.ExerciseName);
                }

                else if (sortBy.Equals("Weight", StringComparison.OrdinalIgnoreCase) ||
                    sortBy.Equals("Kg", StringComparison.OrdinalIgnoreCase))
                {
                    exercise = isAscending ? exercise.OrderBy(e => e.WeightInKg) : exercise.OrderByDescending(e => e.WeightInKg);
                }
            }

            //Pagination
            var skipResults = (pageNumber - 1) * pageSize;

            return await exercise.Skip(skipResults).Take(pageSize).ToListAsync();
        }

        public async Task<Exercise?> GetByIdAsync(Guid id)
        {
            return await dbContext.Exercises.Include(e => e.Intensity).Include(e => e.Workout).FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<Exercise?> UpdateAsync(Guid id, Exercise exercise)
        {
            var existingExercise = await dbContext.Exercises.Include(i => i.Intensity).Include(w => w.Workout).FirstOrDefaultAsync(x => x.Id == id);

            if (existingExercise == null)
            {
                return null;
            }

            var intensity = await dbContext.Intensities.FirstOrDefaultAsync(i => i.Id == exercise.IntensityId);

            if (intensity is null)
            {
                return null;
            }

            var workout = await dbContext.Workouts.FirstOrDefaultAsync(w => w.Id == exercise.WorkoutId);

            if (workout is null)
            {
                return null;
            }

            existingExercise.Workout = workout;
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
