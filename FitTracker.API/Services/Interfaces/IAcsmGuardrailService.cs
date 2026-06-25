using System.Collections.Generic;
using FitTrackr.API.Models.DTO;

namespace FitTrackr.API.Services.Interfaces
{
    public interface IAcsmGuardrailService
    {
        /// <summary>
        /// Validates LLM-generated weight recommendations against ACSM progressive overload guidelines.
        /// Any recommendation exceeding 10% above the user's recent maximum is capped at the safe limit.
        /// </summary>
        GuardrailResult Validate(string llmReply, FitBotContextDto context);
    }

    public sealed record GuardrailResult(
        string SanitizedReply,
        bool Triggered,
        IReadOnlyList<string> InterceptedProgressions);
}
