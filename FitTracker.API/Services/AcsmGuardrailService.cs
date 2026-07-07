using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services.Interfaces;

namespace FitTrackr.API.Services
{
    /// <summary>
    /// Rule-based numerical progressive-overload guardrail.
    /// Enforces: Δweight(e) / weight_prev(e) ≤ 0.10 for recognized structured exercise recommendations.
    /// Violations are automatically replaced with the maximum configured progression.
    /// </summary>
    public class AcsmGuardrailService : IAcsmGuardrailService
    {
        private const double MaxProgressionRate = 0.10;

        // Recognized structured recommendation format:
        // "ExerciseName: N set × M tekrar @ W kg"
        // Also handles "x" instead of "×", optional spacing before "kg", and comma/dot decimals.
        // Free-form chat and historical statements are intentionally outside the automatic intervention scope.
        private static readonly Regex SuggestionLinePattern = new(
            @"^(?<exercise>[^:\r\n]+):\s*\d+\s*set\s*[×x]\s*\d+\s*tekrar\s*@\s*(?<weight>\d+(?:[.,]\d+)?)\s*kg",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        public GuardrailResult Validate(string llmReply, FitBotContextDto context)
        {
            if (string.IsNullOrWhiteSpace(llmReply))
                return new GuardrailResult(llmReply, false, Array.Empty<string>());

            var baseline = BuildBaselineDictionary(context);
            if (baseline.Count == 0)
                return new GuardrailResult(llmReply, false, Array.Empty<string>());

            var intercepted = new List<string>();

            // Automatic intervention is deliberately limited to the recognized structured recommendation format.
            // This avoids changing historical loads, body weight, dates, calories, or other free-form numeric text.
            var sanitized = SuggestionLinePattern.Replace(llmReply, match =>
            {
                var exerciseName = match.Groups["exercise"].Value.Trim();
                var rawWeight = match.Groups["weight"].Value.Replace(',', '.');

                if (!double.TryParse(rawWeight, NumberStyles.Number, CultureInfo.InvariantCulture, out var recommendedKg))
                    return match.Value;

                if (!TryGetBaseline(exerciseName, baseline, out var baselineKg) || baselineKg <= 0)
                    return match.Value;

                var safeMax = baselineKg * (1 + MaxProgressionRate);
                if (recommendedKg <= safeMax)
                    return match.Value;

                var safeStr = safeMax.ToString("F1", CultureInfo.InvariantCulture);

                // Replace exactly the numeric weight and optional whitespace before "kg".
                // This handles "121kg", "121 kg", and "121  kg" without rewriting the remainder of the line.
                var weightGroup = match.Groups["weight"];
                var offsetInMatch = weightGroup.Index - match.Index;
                var matchStr = match.Value;
                var kgPos = matchStr.IndexOf("kg", offsetInMatch + weightGroup.Length, StringComparison.OrdinalIgnoreCase);

                if (kgPos < 0)
                    return match.Value;

                var replacedValue = matchStr[..offsetInMatch] + safeStr + " kg" + matchStr[(kgPos + 2)..];
                if (replacedValue == matchStr)
                    return match.Value;

                intercepted.Add($"{exerciseName}: {recommendedKg:F1} kg → {safeStr} kg (≤10% guardrail)");
                return replacedValue;
            });

            return new GuardrailResult(
                sanitized,
                intercepted.Count > 0,
                intercepted.AsReadOnly());
        }

        // Builds exercise name → most recent max weight dictionary from context.
        private static Dictionary<string, double> BuildBaselineDictionary(FitBotContextDto context)
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var trend in context.WeightTrends)
            {
                if (string.IsNullOrWhiteSpace(trend.ExerciseName))
                    continue;

                var recentMax = trend.WeeklyMaxWeights
                    .Where(w => w.MaxKg > 0)
                    .OrderBy(w => w.WeeksAgo)
                    .FirstOrDefault();

                if (recentMax != null)
                    dict[trend.ExerciseName.Trim()] = recentMax.MaxKg;
            }

            // Fill in exercises from recent workouts that are not in WeightTrends.
            foreach (var workout in context.RecentWorkouts)
            {
                foreach (var ex in workout.Exercises)
                {
                    if (!string.IsNullOrWhiteSpace(ex.ExerciseName) && ex.MaxWeightKg > 0)
                        dict.TryAdd(ex.ExerciseName.Trim(), ex.MaxWeightKg);
                }
            }

            return dict;
        }

        private static bool TryGetBaseline(
            string exerciseName,
            Dictionary<string, double> baseline,
            out double baselineKg)
        {
            if (baseline.TryGetValue(exerciseName, out baselineKg))
                return true;

            // Partial/substring match as fallback (e.g. "Bench Press" matches "Bench Press (Dumbbell)").
            foreach (var key in baseline.Keys)
            {
                if (key.Contains(exerciseName, StringComparison.OrdinalIgnoreCase) ||
                    exerciseName.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    baselineKg = baseline[key];
                    return true;
                }
            }

            baselineKg = 0;
            return false;
        }
    }
}