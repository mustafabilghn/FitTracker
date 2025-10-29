using FitTrackr.API.Data;
using FitTrackr.API.Models.DTO;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace FitTrackr.API.Validations
{
    public class ExerciseRequestDtoValidator : AbstractValidator<ExerciseRequestDto>
    {
        private readonly FitTrackrDbContext dbContext;

        public ExerciseRequestDtoValidator(FitTrackrDbContext dbContext)
        {
            this.dbContext = dbContext;

            RuleFor(e => e.ExerciseName)
                .NotEmpty()
                .WithMessage("Exercise name is required.")
                .MaximumLength(25)
                .WithMessage("Exercise name cannot exceed 25 characters.");

            RuleFor(i => i.IntensityId)
                .NotEmpty()
                .WithMessage("Intensity ID is required.")
                .MustAsync(async (id, cancellationToken) =>
                {
                    return await dbContext.Intensities.AnyAsync(i => i.Id == id, cancellationToken);
                }).WithMessage("Intensity with given ID does not exist.");

            RuleFor(w => w.WorkoutId)
                .NotEmpty()
                .WithMessage("Workout ID is required.")
                .MustAsync(async (id, cancellationToken) =>
                {
                    return await dbContext.Workouts.AnyAsync(w => w.Id == id, cancellationToken);
                }).WithMessage("Workout with given ID does not exist.");
        }
    }
}
