using System.Collections.Generic;

namespace FitTrackr.API.Models.DTO
{
    public class FitBotChatRequestDto
    {
        public string Message { get; set; } = string.Empty;
        public string ActionType { get; set; } = "free";
        public List<FitBotConversationMessageDto> ConversationHistory { get; set; } = new();
    }

    public class FitBotConversationMessageDto
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
