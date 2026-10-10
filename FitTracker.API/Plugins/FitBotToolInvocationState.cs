using System.Text.Json;

namespace FitTrackr.API.Plugins;

public interface IFitBotToolInvocationState
{
    bool PersonalReadToolSucceeded { get; }
    void Record(string toolName, object? result);
}

public sealed class FitBotToolInvocationState : IFitBotToolInvocationState
{
    private static readonly HashSet<string> PersonalReadTools = new(StringComparer.Ordinal)
    {
        "Workout-GetRecentWorkouts",
        "Workout-GetWeightTrends",
        "Workout-GetPlateauExercises",
        "WorkoutPlan-GetPlannedWorkouts"
    };

    public bool PersonalReadToolSucceeded { get; private set; }

    public void Record(string toolName, object? result)
    {
        if (!PersonalReadTools.Contains(toolName) || result is null)
            return;

        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(result));
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out _))
                return;
        }
        catch (JsonException)
        {
            return;
        }

        PersonalReadToolSucceeded = true;
    }
}
