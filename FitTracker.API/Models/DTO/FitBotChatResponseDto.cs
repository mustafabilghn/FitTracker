using System.Collections.Generic;

namespace FitTrackr.API.Models.DTO
{
    public class FitBotChatResponseDto
    {
        public string Reply { get; set; } = string.Empty;
        public List<string> PlateauAlerts { get; set; } = new();

        /// <summary>
        /// True when the ACSM guardrail intercepted one or more unsafe weight progressions.
        /// </summary>
        public bool GuardrailTriggered { get; set; }

        /// <summary>
        /// Human-readable descriptions of each intercepted progression
        /// (e.g. "Bench Press: 121.0 kg → 110.0 kg (ACSM ≤10% kuralı)").
        /// Empty when GuardrailTriggered is false.
        /// </summary>
        public List<string> InterceptedProgressions { get; set; } = new();
    }
}

