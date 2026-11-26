using System.Text.Json;

namespace FitTrackr.API.Plugins;

public interface IFitBotToolInvocationState
{
    IReadOnlyDictionary<string, string> SuccessfulResults { get; }
    IReadOnlyDictionary<string, string> ObservedKnowledgeResults { get; }

    bool HasSuccessfulResult(string toolName);
    bool WasAttempted(string toolName);
    void MarkAttempted(string toolName);
    void Record(string toolName, object? result);
    bool HasUnavailableResult(string toolName);

    /// <summary>Herhangi bir kişisel okuma tool'u (Workout/WorkoutPlan okuma) bu istekte denendi mi.</summary>
    bool WasAnyPersonalToolAttempted { get; }

    /// <summary>Herhangi bir kişisel okuma tool'u bu istekte hatasız sonuç döndürdü mü.</summary>
    bool HasSuccessfulPersonalResult { get; }
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

    private readonly Dictionary<string, string> _successfulResults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _observedKnowledgeResults =
    new(StringComparer.Ordinal);
    private readonly HashSet<string> _attemptedTools = new(StringComparer.Ordinal);

    private static bool IsTrackedTool(string toolName) =>
    PersonalReadTools.Contains(toolName) ||
    string.Equals(
        toolName,
        "Knowledge-SearchFitnessKnowledge",
        StringComparison.Ordinal);

    public bool WasAttempted(string toolName) =>
        _attemptedTools.Contains(toolName);

    public void MarkAttempted(string toolName)
    {
        if (!IsTrackedTool(toolName))
            return;

        _attemptedTools.Add(toolName);
        _unavailableResults.Remove(toolName);
    }
    private readonly HashSet<string> _unavailableResults =
    new(StringComparer.Ordinal);

    public bool HasUnavailableResult(string toolName) =>
        _unavailableResults.Contains(toolName);

    public IReadOnlyDictionary<string, string> SuccessfulResults => _successfulResults;
    public IReadOnlyDictionary<string, string> ObservedKnowledgeResults =>
    _observedKnowledgeResults;

    public void Record(string toolName, object? result)
    {
        if (!IsTrackedTool(toolName))
            return;

        MarkAttempted(toolName);

        if (result is null)
            return;

        try
        {
            var serialized = result is string text ? text : JsonSerializer.Serialize(result);
            using var document = JsonDocument.Parse(serialized);
            var root = document.RootElement;

            if (string.Equals(
        toolName,
        "Knowledge-SearchFitnessKnowledge",
        StringComparison.Ordinal) &&
    root.ValueKind == JsonValueKind.Object)
            {
                _observedKnowledgeResults[toolName] = serialized;
            }

            // Knowledge search sonucunun kullanılamadığını ayırt et.
            if (string.Equals(
                    toolName,
                    "Knowledge-SearchFitnessKnowledge",
                    StringComparison.Ordinal) &&
                root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("status", out var unavailableStatus) &&
                unavailableStatus.ValueKind == JsonValueKind.String &&
                string.Equals(
                    unavailableStatus.GetString(),
                    "unavailable",
                    StringComparison.OrdinalIgnoreCase))
            {
                _unavailableResults.Add(toolName);
                return;
            }

            // Mevcut genel error kontrolü.
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("error", out _))
                return;

            if (string.Equals(toolName, "Knowledge-SearchFitnessKnowledge", StringComparison.Ordinal))
            {
                if (!root.TryGetProperty("status", out var status) ||
                    !string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase) ||
                    !root.TryGetProperty("results", out var results) ||
                    results.ValueKind != JsonValueKind.Array ||
                    results.GetArrayLength() == 0)
                    return;
            }

            _successfulResults[toolName] = serialized;
        }
        catch (JsonException)
        {
            return;
        }
    }

    public bool HasSuccessfulResult(string toolName) => _successfulResults.ContainsKey(toolName);

    public bool WasAnyPersonalToolAttempted => _attemptedTools.Overlaps(PersonalReadTools);

    public bool HasSuccessfulPersonalResult => _successfulResults.Keys.Any(PersonalReadTools.Contains);
}
