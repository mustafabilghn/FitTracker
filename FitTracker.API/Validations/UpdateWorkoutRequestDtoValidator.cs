using FitTrackr.API.Models.DTO;
using FluentValidation;

namespace FitTrackr.API.Validations
{
    public class UpdateWorkoutRequestDtoValidator : AbstractValidator<UpdateWorkoutRequestDto>
    {
        public UpdateWorkoutRequestDtoValidator()
        {
            RuleFor(e => e.WorkoutName)
                .NotEmpty().WithMessage("Workout name is required.")
                .MaximumLength(20).WithMessage("Workout name cannot exceed 20 characters.");

            RuleFor(w => w.WorkoutDate)
                .Must(d => d != default && d.Year >= 1900)
                .WithMessage("Workout date is required and must be valid.");
        }
    }
}
