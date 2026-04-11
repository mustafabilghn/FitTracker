namespace FitTrackr.MAUI.Models;

public class FitBotChatMessage
{
    public string Text { get; set; } = string.Empty;

    public bool IsFromUser { get; set; }

    /// <summary>Bot sol, kullanıcı sağ hizalama için.</summary>
    public LayoutOptions BubbleAlignment => IsFromUser ? LayoutOptions.End : LayoutOptions.Start;
}
