using System.Collections.Generic;
using System.Linq;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// Unit tests for AcsmGuardrailService.Validate().
/// Production code is NOT modified regardless of test outcomes.
/// Failing tests include an explanation of the exact root cause.
/// </summary>
public class AcsmGuardrailServiceTests
{
    private readonly AcsmGuardrailService _sut = new();

    // ── Shared context ────────────────────────────────────────────────────────
    // Bench Press baseline : 100 kg  (WeeksAgo = 0, most recent)
    //   safe-max            : 110 kg  (100 × 1.10)
    // Squat       baseline : 120 kg  (WeeksAgo = 0)
    //   safe-max            : 132 kg  (120 × 1.10)
    private static FitBotContextDto BuildContext() => new()
    {
        TotalWorkouts = 10,
        WeightTrends = new List<ExerciseWeightTrendDto>
        {
            new()
            {
                ExerciseName = "Bench Press",
                Trend = "artıyor",
                WeeklyMaxWeights = new List<WeeklyMaxWeightDto>
                {
                    new() { WeeksAgo = 0, MaxKg = 100.0 },
                    new() { WeeksAgo = 1, MaxKg = 97.5  },
                }
            },
            new()
            {
                ExerciseName = "Squat",
                Trend = "artıyor",
                WeeklyMaxWeights = new List<WeeklyMaxWeightDto>
                {
                    new() { WeeksAgo = 0, MaxKg = 120.0 },
                    new() { WeeksAgo = 1, MaxKg = 117.5 },
                }
            }
        },
        RecentWorkouts = new List<WorkoutContextEntryDto>()
    };

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 1 — Unsafe structured suggestion
    // Input  : "Bench Press: 3 set × 10 tekrar @ 121 kg"
    // 121 > safe-max 110 → must be capped to 110.0 kg
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Test1_UnsafeStructuredSuggestion_IsInterceptedAndCapped()
    {
        const string input = "Bench Press: 3 set × 10 tekrar @ 121 kg";
        var result = _sut.Validate(input, BuildContext());

        Assert.True(result.Triggered);
        Assert.DoesNotContain("121 kg", result.SanitizedReply);
        Assert.Contains("110.0 kg", result.SanitizedReply);
        Assert.True(
            result.InterceptedProgressions.Any(s => s.Contains("Bench Press")),
            "InterceptedProgressions must mention Bench Press.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 2 — Exactly 10 % suggestion: 110 kg == safe-max → must NOT trigger
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Test2_ExactlyTenPercentSuggestion_IsNotIntercepted()
    {
        const string input = "Bench Press: 3 set × 10 tekrar @ 110 kg";
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Equal(input, result.SanitizedReply);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 3 — Safe recommendation: 105 kg < safe-max 110 kg
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Test3_SafeRecommendation_PassesThroughUnchanged()
    {
        const string input = "Bench Press: 3 set × 10 tekrar @ 105 kg";
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Equal(input, result.SanitizedReply);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 4 — Multiple exercises
    // Bench Press 125 kg  unsafe  (safe-max = 110)  → capped
    // Squat       130 kg  SAFE    (safe-max = 132)  → unchanged
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Test4_MultipleExercises_OnlyUnsafeOneIsIntercepted()
    {
        const string input =
            "Bench Press: 3 set × 10 tekrar @ 125 kg\n" +
            "Squat: 3 set × 8 tekrar @ 130 kg";

        var result = _sut.Validate(input, BuildContext());

        Assert.True(result.Triggered);
        Assert.Contains("110.0 kg", result.SanitizedReply);
        Assert.Contains("130 kg", result.SanitizedReply);
        Assert.Single(result.InterceptedProgressions);
        Assert.True(result.InterceptedProgressions.Any(s => s.Contains("Bench Press")));
        Assert.False(result.InterceptedProgressions.Any(s => s.Contains("Squat")));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 5 — Exercise not in context (no baseline)
    // "Cable Fly" has no WeightTrend entry → guardrail cannot apply → pass-through
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Test5_NoBaselineForExercise_PassesThroughUnchanged()
    {
        const string input = "Cable Fly: 3 set × 12 tekrar @ 150 kg";
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Equal(input, result.SanitizedReply);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 6a — Alternate format: weight without space before "kg"  (@ 121kg)
    //
    // EXPECTED CORRECT BEHAVIOUR: trigger + cap to 110.0 kg
    //
    // ACTUAL BEHAVIOUR (BUG in current implementation):
    //   Primary pass replacement:
    //     match.Value.Replace("121 kg", "110.0 kg")
    //   match.Value = "...@ 121kg"  — no space before "kg"
    //   "121 kg" is NOT a substring of "121kg"
    //   → string.Replace finds nothing and returns the original match value
    //   → text is NOT modified in the output
    //   BUT intercepted.Add() was already executed BEFORE the return
    //   → Triggered = true  (intercepted list is non-empty)
    //   → SanitizedReply still contains "121kg"  (text unchanged)
    //
    // FAILING ASSERTIONS:
    //   Assert.DoesNotContain("121", ...)  — FAILS  (still "121kg" in reply)
    //   Assert.Contains("110.0 kg", ...)   — FAILS  (replacement never happened)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Test6a_NoSpaceBeforeKg_GuardrailCorrectlyModifiesReply()
    {
        const string input = "Bench Press: 3 set x 10 tekrar @ 121kg";
        var result = _sut.Validate(input, BuildContext());

        Assert.True(result.Triggered);
        Assert.DoesNotContain("121", result.SanitizedReply);
        Assert.Contains("110.0 kg", result.SanitizedReply);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 6b — Alternate format: double space before "kg"  (@ 121  kg)
    //
    // EXPECTED CORRECT BEHAVIOUR: trigger + cap to 110.0 kg
    //
    // ACTUAL BEHAVIOUR (BUG in current implementation):
    //   match.Value.Replace("121 kg", "110.0 kg")
    //   "121  kg" (two spaces) does NOT contain "121 kg" (one space)
    //   → string.Replace returns original
    //   → intercepted.Add() already ran → Triggered = true
    //   → text still "@ 121  kg"
    //
    // FAILING ASSERTIONS: same as 6a
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Test6b_DoubleSpaceBeforeKg_GuardrailCorrectlyModifiesReply()
    {
        const string input = "Bench Press: 3 set x 10 tekrar @ 121  kg";
        var result = _sut.Validate(input, BuildContext());

        Assert.True(result.Triggered);
        Assert.DoesNotContain("121", result.SanitizedReply);
        Assert.Contains("110.0 kg", result.SanitizedReply);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 6c — Alternate format: decimal weight with comma  (@ 121,5 kg)
    //
    // EXPECTED CORRECT BEHAVIOUR: 121.5 > 110 → cap to 110.0 kg
    //
    // ACTUAL BEHAVIOUR: PASSES
    //   match.Groups["weight"].Value = "121,5"
    //   Replacement: match.Value.Replace("121,5 kg", "110.0 kg")
    //   "121,5 kg" IS present in the match value → replacement SUCCEEDS.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Test6c_CommaDecimalWeight_GuardrailCorrectlyModifiesReply()
    {
        const string input = "Bench Press: 3 set x 10 tekrar @ 121,5 kg";
        var result = _sut.Validate(input, BuildContext());

        Assert.True(result.Triggered);
        Assert.DoesNotContain("121,5", result.SanitizedReply);
        Assert.Contains("110.0 kg", result.SanitizedReply);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 7 — Free-text recommendation
    // "Bugün Bench Press için 121 kg deneyebilirsin."
    // Secondary pass: exercise name found on line, 121 > 110 → intercept
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Test7_FreeTextRecommendation_IsInterceptedAndCapped()
    {
        const string input = "Bugün Bench Press için 121 kg deneyebilirsin.";
        var result = _sut.Validate(input, BuildContext());

        Assert.True(result.Triggered);
        Assert.DoesNotContain("121 kg", result.SanitizedReply);
        Assert.Contains("110.0 kg", result.SanitizedReply);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEST 8 — False-positive protection
    // Same line contains "Bench Press" AND "75 kg" (body weight, not a load)
    // 75 < safe-max 110 → rule not violated → no change, no trigger
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Test8_FalsePositiveProtection_BodyWeightNotIntercepted()
    {
        const string input =
            "Bench Press yapmadan önce vücut ağırlığın 75 kg olabilir. 3 set 10 tekrar yap.";
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Contains("75 kg", result.SanitizedReply);
    }
}
