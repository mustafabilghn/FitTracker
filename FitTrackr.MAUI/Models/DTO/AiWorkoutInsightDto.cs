namespace FitTrackr.MAUI.Models.DTO;

public class AiWorkoutInsightDto
{
    public string Summary { get; set; } = string.Empty;

    public List<string> Strengths { get; set; } = new();

    public List<string> Improvements { get; set; } = new();

    public string NextWorkoutSuggestion { get; set; } = string.Empty;
}
