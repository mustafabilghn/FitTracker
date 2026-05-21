using System.Collections.Generic;

namespace FitTrackr.MAUI.Models.DTO;

public class FitBotChatRequestDto
{
    public string Message { get; set; } = string.Empty;
    public string ActionType { get; set; } = "free";
    public List<FitBotConversationMessage> ConversationHistory { get; set; } = new();
}

public class FitBotConversationMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class FitBotChatResponseDto
{
    public string Reply { get; set; } = string.Empty;
    public List<string> PlateauAlerts { get; set; } = new();
}
