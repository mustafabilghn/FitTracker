using FitTrackr.API.Data;
using FitTrackr.API.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitTrackr.API.Repositories
{
    public class ExerciseSetRepository : IExerciseSetRepository
    {
        private readonly FitTrackrDbContext dbContext;

        public ExerciseSetRepository(FitTrackrDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<ExerciseSet> CreateAsync(ExerciseSet exerciseSet)
        {
            await dbContext.ExerciseSets.AddAsync(exerciseSet);
            await dbContext.SaveChangesAsync();

            return exerciseSet;
        }

        public async Task<ExerciseSet?> DeleteAsync(Guid id)
        {
            var existingSet = await dbContext.ExerciseSets.FirstOrDefaultAsync(x => x.Id == id);

            if (existingSet == null)
                return null;

            dbContext.ExerciseSets.Remove(existingSet);
            await dbContext.SaveChangesAsync();

            return existingSet;
        }

        public async Task<List<ExerciseSet>> GetAllAsync()
        {
            return await dbContext.ExerciseSets.ToListAsync();
        }

        public async Task<ExerciseSet?> GetByIdAsync(Guid id)
        {
            return await dbContext.ExerciseSets.FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<ExerciseSet?> UpdateAsync(Guid id, ExerciseSet exerciseSet)
        {
            var existingSet = await dbContext.ExerciseSets.FirstOrDefaultAsync(x => x.Id == id);

            if (existingSet == null)
                return null;

            existingSet.SetNumber = exerciseSet.SetNumber;
            existingSet.Reps = exerciseSet.Reps;
            existingSet.WeightInKg = exerciseSet.WeightInKg;

            await dbContext.SaveChangesAsync();

            return existingSet;
        }
    }
}
