using System.Collections.Generic;

namespace FitTrackr.API.Models.DTO
{
    public class FitBotChatResponseDto
    {
        public string Reply { get; set; } = string.Empty;
        public List<string> PlateauAlerts { get; set; } = new();
    }
}
