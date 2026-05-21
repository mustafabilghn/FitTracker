using FluentValidation;
using FitTrackr.API.Models.DTO;
using System.Collections.Generic;

namespace FitTrackr.API.Validations
{
    public class FitBotChatRequestDtoValidator : AbstractValidator<FitBotChatRequestDto>
    {
        private static readonly HashSet<string> ValidActionTypes =
            new(System.StringComparer.OrdinalIgnoreCase) { "free", "analyze", "today", "program", "motivation" };

        public FitBotChatRequestDtoValidator()
        {
            RuleFor(x => x.Message)
                .NotEmpty().WithMessage("Mesaj boş olamaz.")
                .MaximumLength(1000).WithMessage("Mesaj en fazla 1000 karakter olabilir.");

            RuleFor(x => x.ActionType)
                .NotEmpty().WithMessage("Aksiyon tipi gereklidir.")
                .Must(a => ValidActionTypes.Contains(a))
                .WithMessage("Geçersiz aksiyon tipi. Geçerli değerler: free, analyze, today, program, motivation.");
        }
    }
}
