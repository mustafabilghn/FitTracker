using FitTrackr.API.Data;
using FitTrackr.API.Models.DTO;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace FitTrackr.API.Validations
{
    public class WorkoutRequestDtoValidator : AbstractValidator<WorkoutRequestDto>
    {
        private readonly FitTrackrDbContext dbContext;

        public WorkoutRequestDtoValidator(FitTrackrDbContext dbContext)
        {
            this.dbContext = dbContext;

            RuleFor(e => e.WorkoutName)
                .NotEmpty().WithMessage("Workout name is required.")
                 .MaximumLength(20)
                 .WithMessage("Workout name cannot exceed 20 characters.");

            RuleFor(e => e.WorkoutDate)
                .Must(day => Enum.IsDefined(typeof(DayOfWeek), day)).WithMessage("Workout day is required and must be valid.");

            RuleFor(e => e.DurationMinutes)
                .GreaterThan(0)
                .WithMessage("Duration must be greater than 0 minutes.");

            RuleFor(e => e.LocationId)
                .MustAsync(LocationExists)
                .WithMessage("Location with given ID does not exist.");


        }

        private async Task<bool> LocationExists(Guid locationId, CancellationToken cancellationToken)
        {
            if (locationId == Guid.Empty)
            {
                return false;
            }

            return await dbContext.Locations.AnyAsync(l => l.Id == locationId, cancellationToken);
        }
    }
}
