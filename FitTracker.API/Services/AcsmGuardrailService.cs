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
    /// Rule-based ACSM progressive overload guardrail.
    /// Enforces: Δweight(e) / weight_prev(e) ≤ 0.10 for all exercise weight recommendations.
    /// Violations are automatically replaced with the maximum safe progression.
    /// </summary>
    public class AcsmGuardrailService : IAcsmGuardrailService
    {
        // Numerical rule (≤10%) and baseline derivation live in AcsmProgressionRule (shared with plan persistence).

        // Matches structured suggestion lines in either language:
        // TR: "ExerciseName: N set × M tekrar @ W kg"  |  EN: "ExerciseName: N sets × M reps @ W kg"
        // Also handles "x" instead of "×", optional "s" on set/rep, and optional comma/dot in weight.
        private static readonly Regex SuggestionLinePattern = new(
            @"^(?<exercise>[^:\r\n]+):\s*\d+\s*sets?\s*[×x]\s*\d+\s*(?:tekrar|reps?)\s*@\s*(?<weight>\d+(?:[.,]\d+)?)\s*kg",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        // Fallback: inline weight mentions on a line that already contains a known exercise
        private static readonly Regex InlineWeightPattern = new(
            @"(?<weight>\d+(?:[.,]\d+)?)\s*kg",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public GuardrailResult Validate(string llmReply, FitBotContextDto context)
        {
            if (string.IsNullOrWhiteSpace(llmReply))
                return new GuardrailResult(llmReply, false, Array.Empty<string>());

            var baseline = AcsmProgressionRule.BuildBaselines(context);
            if (baseline.Count == 0)
                return new GuardrailResult(llmReply, false, Array.Empty<string>());

            var intercepted = new List<string>();
            var sanitized = llmReply;

            // Primary pass: structured suggestion lines ("Exercise: N sets × M reps @ W kg")
            sanitized = SuggestionLinePattern.Replace(sanitized, match =>
            {
                var exerciseName = match.Groups["exercise"].Value.Trim();
                var rawWeight = match.Groups["weight"].Value.Replace(',', '.');

                if (!double.TryParse(rawWeight, NumberStyles.Number, CultureInfo.InvariantCulture, out var recommendedKg))
                    return match.Value;

                if (!TryGetBaseline(exerciseName, baseline, out var baselineKg) || baselineKg <= 0)
                    return match.Value;

                var safeMax = AcsmProgressionRule.SafeMaxKg(baselineKg);
                if (AcsmProgressionRule.IsWithinLimit(recommendedKg, baselineKg))
                    return match.Value;

                var safeStr = safeMax.ToString("F1", CultureInfo.InvariantCulture);

                // Use group index to replace exactly the weight digits + optional surrounding spaces + "kg".
                // This handles "121kg", "121 kg", "121  kg" — avoids the silent-fail of string.Replace.
                var weightGroup = match.Groups["weight"];
                var offsetInMatch = weightGroup.Index - match.Index;
                var matchStr = match.Value;
                var kgPos = matchStr.IndexOf("kg", offsetInMatch + weightGroup.Length, StringComparison.OrdinalIgnoreCase);

                if (kgPos < 0)
                    return match.Value; // unexpected: no "kg" found, leave unchanged

                var replacedValue = matchStr[..offsetInMatch] + safeStr + " kg" + matchStr[(kgPos + 2)..];

                if (replacedValue == matchStr)
                    return match.Value; // nothing actually changed, don't intercept

                intercepted.Add($"{exerciseName}: {recommendedKg:F1} kg → {safeStr} kg (ACSM ≤10% rule)");
                return replacedValue;
            });

            // Secondary pass: free-form lines that contain a known exercise name and a weight value
            // Only applies to lines NOT already handled (i.e., not the structured format above)
            var lines = sanitized.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Skip lines that were already matched by SuggestionLinePattern
                if (SuggestionLinePattern.IsMatch(line))
                    continue;

                // Find if a known exercise appears in this line
                if (!TryMatchExercise(line, baseline, out var exerciseName, out var baselineKg) || baselineKg <= 0)
                    continue;

                var safeMax = AcsmProgressionRule.SafeMaxKg(baselineKg);

                lines[i] = InlineWeightPattern.Replace(line, weightMatch =>
                {
                    var raw = weightMatch.Groups["weight"].Value.Replace(',', '.');
                    if (!double.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var kg))
                        return weightMatch.Value;

                    if (AcsmProgressionRule.IsWithinLimit(kg, baselineKg))
                        return weightMatch.Value;

                    // The clause itself warns that this value is over the safety limit ("115 kg … %110 sınırını aşıyor"):
                    // it is not a recommendation. Rewriting only the number would produce a self-contradiction
                    // ("110.0 kg … sınırını aşıyor"), so leave it as written.
                    if (IsFlaggedAsOverSafetyLimit(line, weightMatch.Index))
                        return weightMatch.Value;

                    var safeStr = safeMax.ToString("F1", CultureInfo.InvariantCulture);
                    intercepted.Add($"{exerciseName}: {kg:F1} kg → {safeStr} kg (ACSM ≤10% rule)");
                    return $"{safeStr} kg";
                });
            }

            if (intercepted.Count > 0)
                sanitized = string.Join('\n', lines);

            return new GuardrailResult(
                sanitized,
                intercepted.Count > 0,
                intercepted.AsReadOnly());
        }

        // Explicit "exceeds" statements only. Negations ("aşmaz", "aşmıyor", "does not exceed") and other subjects
        // ("rekorunu aşıyorsun") deliberately do not match, so those values are still capped.
        private static readonly Regex ExceedsVerbPattern = new(
            @"\baş(ıyor|ar|an|tığı|tığından|maktadır)\b|\bexceeds\b|\bexceeding\b|\bis (over|above) the\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Explicit refusal/rejection of the value ("kabul edilmiyor", "izin verilmiyor", "uygun değil", "is rejected",
        // "not allowed"). Used together with a safety-limit reference, e.g. "… %110'undan yüksek olduğu için kabul edilmiyor".
        private static readonly Regex RefusalPattern = new(
            @"kabul edil(miyor|mez|emez)|izin veril(miyor|mez)|uygun değil|önerilmez|reddedil\w*" +
            @"|onayla(mıyor|maz)|onaylan(mıyor|maz)|kaydedil(miyor|mez|emez)" +
            @"|\b(is |was |are )?rejected\b|\bnot (allowed|permitted|accepted|recommended|approved)\b|\bcannot be (accepted|allowed|saved)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SafetyLimitPattern = new(
            @"sınır|limit|güvenli|safe|%\s*1?10\b|acsm",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // A clause that tells the user to do/use the weight is a recommendation, never a warning — always capped.
        private static readonly Regex RecommendationPattern = new(
            @"\b(öneriyorum|öneririm|yap|yapabilirsin|çıkar|çıkabilirsin|dene|deneyebilirsin)\b|\bi (recommend|suggest)\b|\btry\b|\bgo for\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // True when the clause containing the weight is a warning/refusal about a safety limit (not a recommendation):
        // it references a safety limit AND either says the value exceeds it or explicitly refuses it, and it does not
        // tell the user to use the weight. Only the clause around the number counts.
        private static bool IsFlaggedAsOverSafetyLimit(string line, int index)
        {
            var clause = ClauseAround(line, index);
            return SafetyLimitPattern.IsMatch(clause)
                   && (ExceedsVerbPattern.IsMatch(clause) || RefusalPattern.IsMatch(clause))
                   && !RecommendationPattern.IsMatch(clause);
        }

        // Clause = text between sentence/clause delimiters (. ! ? ;). A '.' between digits (decimal weight) is not a delimiter.
        private static string ClauseAround(string line, int index)
        {
            var start = index;
            while (start > 0 && !IsClauseDelimiter(line, start - 1))
                start--;

            var end = index;
            while (end < line.Length && !IsClauseDelimiter(line, end))
                end++;

            return line[start..end];
        }

        private static bool IsClauseDelimiter(string text, int i) => text[i] switch
        {
            '!' or '?' or ';' => true,
            '.' => !(i > 0 && char.IsDigit(text[i - 1]) && i + 1 < text.Length && char.IsDigit(text[i + 1])),
            _ => false
        };

        private static bool TryGetBaseline(
            string exerciseName,
            Dictionary<string, double> baseline,
            out double baselineKg)
        {
            if (baseline.TryGetValue(exerciseName, out baselineKg))
                return true;

            // Partial/substring match as fallback (e.g. "Bench Press" matches "Bench Press (Dumbbell)")
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

        private static bool TryMatchExercise(
            string line,
            Dictionary<string, double> baseline,
            out string exerciseName,
            out double baselineKg)
        {
            foreach (var kvp in baseline)
            {
                if (line.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    exerciseName = kvp.Key;
                    baselineKg = kvp.Value;
                    return true;
                }
            }

            exerciseName = string.Empty;
            baselineKg = 0;
            return false;
        }
    }
}
