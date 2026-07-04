using System.Collections.Generic;
using System.Linq;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services;
using Xunit;

namespace FitTracker.API.Tests;

// This constructed test set evaluates only recognized exercise names, available baselines,
// and supported numerical recommendation formats. It is not a clinical safety benchmark or
// comprehensive injury-prevention evaluation.
//
// Scope: exactly 30 independent cases where (a) the exercise name is recognized in context,
// (b) a prior baseline weight exists for that exercise, (c) the LLM-suggested weight exceeds
// baseline x 1.10 in one of the guardrail's supported numerical formats, and (d) the guardrail
// must cap the suggestion to baseline x 1.10 and report exactly one interception.
//
// This file is additive: it does not modify, remove, or replace any test in
// AcsmGuardrailServiceTests.cs.
public class NumericalGuardrailSafetySetTests
{
    private readonly AcsmGuardrailService _sut = new();

    // Builds a context containing a baseline for exactly one exercise, so each case is
    // fully independent of every other exercise/baseline combination.
    private static FitBotContextDto BuildContextFor(string exerciseName, double baselineKg) => new()
    {
        TotalWorkouts = 1,
        WeightTrends = new List<ExerciseWeightTrendDto>
        {
            new()
            {
                ExerciseName = exerciseName,
                Trend = "artıyor",
                WeeklyMaxWeights = new List<WeeklyMaxWeightDto>
                {
                    new() { WeeksAgo = 0, MaxKg = baselineKg },
                }
            }
        },
        RecentWorkouts = new List<WorkoutContextEntryDto>()
    };

    public static IEnumerable<object[]> UnsafeOverloadCases()
    {
        // Each row: exerciseName, baselineKg, input line (supported format), expected safe
        // weight string (baseline x 1.10, one decimal), and the original unsafe weight token
        // that must no longer appear verbatim in the sanitized reply.
        var cases = new (string ExerciseName, double BaselineKg, string Input, string ExpectedSafeWeight, string OriginalToken)[]
        {
            // Bench Press — baseline 100 kg → safe max 110.0 kg
            ("Bench Press",            100.0, "Bench Press: 3 set × 10 tekrar @ 125 kg",        "110.0", "125 kg"),
            ("Bench Press",            100.0, "Bench Press: 3 set x 10 tekrar @ 130kg",          "110.0", "130kg"),
            ("Bench Press",            100.0, "Bench Press: 3 set x 10 tekrar @ 140  kg",        "110.0", "140  kg"),
            ("Bench Press",            100.0, "Bench Press: 3 set × 10 tekrar @ 125,5 kg",       "110.0", "125,5 kg"),

            // Squat — baseline 120 kg → safe max 132.0 kg
            ("Squat",                  120.0, "Squat: 3 set × 10 tekrar @ 150 kg",               "132.0", "150 kg"),
            ("Squat",                  120.0, "Squat: 3 set x 10 tekrar @ 145kg",                "132.0", "145kg"),
            ("Squat",                  120.0, "Squat: 3 set x 10 tekrar @ 160  kg",              "132.0", "160  kg"),
            ("Squat",                  120.0, "Squat: 3 set × 10 tekrar @ 133,5 kg",             "132.0", "133,5 kg"),

            // Deadlift — baseline 140 kg → safe max 154.0 kg
            ("Deadlift",               140.0, "Deadlift: 3 set × 10 tekrar @ 170 kg",            "154.0", "170 kg"),
            ("Deadlift",               140.0, "Deadlift: 3 set x 10 tekrar @ 180kg",             "154.0", "180kg"),
            ("Deadlift",               140.0, "Deadlift: 3 set x 10 tekrar @ 200  kg",           "154.0", "200  kg"),
            ("Deadlift",               140.0, "Deadlift: 3 set × 10 tekrar @ 155,5 kg",          "154.0", "155,5 kg"),

            // Overhead Press — baseline 65 kg → safe max 71.5 kg
            ("Overhead Press",         65.0,  "Overhead Press: 3 set × 10 tekrar @ 80 kg",       "71.5", "80 kg"),
            ("Overhead Press",         65.0,  "Overhead Press: 3 set x 10 tekrar @ 85kg",        "71.5", "85kg"),
            ("Overhead Press",         65.0,  "Overhead Press: 3 set x 10 tekrar @ 90  kg",      "71.5", "90  kg"),
            ("Overhead Press",         65.0,  "Overhead Press: 3 set × 10 tekrar @ 72,5 kg",     "71.5", "72,5 kg"),

            // Barbell Row — baseline 75 kg → safe max 82.5 kg
            ("Barbell Row",            75.0,  "Barbell Row: 3 set × 10 tekrar @ 95 kg",          "82.5", "95 kg"),
            ("Barbell Row",            75.0,  "Barbell Row: 3 set x 10 tekrar @ 100kg",          "82.5", "100kg"),
            ("Barbell Row",            75.0,  "Barbell Row: 3 set x 10 tekrar @ 110  kg",        "82.5", "110  kg"),
            ("Barbell Row",            75.0,  "Barbell Row: 3 set × 10 tekrar @ 83,5 kg",        "82.5", "83,5 kg"),

            // Leg Press — baseline 30 kg → safe max 33.0 kg
            ("Leg Press",              30.0,  "Leg Press: 3 set × 10 tekrar @ 45 kg",            "33.0", "45 kg"),
            ("Leg Press",              30.0,  "Leg Press: 3 set x 10 tekrar @ 50kg",             "33.0", "50kg"),

            // Hip Thrust — baseline 30 kg → safe max 33.0 kg
            ("Hip Thrust",             30.0,  "Hip Thrust: 3 set x 10 tekrar @ 40  kg",          "33.0", "40  kg"),
            ("Hip Thrust",             30.0,  "Hip Thrust: 3 set × 10 tekrar @ 34,5 kg",         "33.0", "34,5 kg"),

            // Lat Pulldown — baseline 90 kg → safe max 99.0 kg
            ("Lat Pulldown",           90.0,  "Lat Pulldown: 3 set × 10 tekrar @ 110 kg",        "99.0", "110 kg"),
            ("Lat Pulldown",           90.0,  "Lat Pulldown: 3 set x 10 tekrar @ 115kg",         "99.0", "115kg"),

            // Cable Row — baseline 60 kg → safe max 66.0 kg
            ("Cable Row",              60.0,  "Cable Row: 3 set x 10 tekrar @ 75  kg",           "66.0", "75  kg"),
            ("Cable Row",              60.0,  "Cable Row: 3 set × 10 tekrar @ 67,5 kg",          "66.0", "67,5 kg"),

            // Bicep Curl — baseline 45 kg → safe max 49.5 kg
            ("Bicep Curl",             45.0,  "Bicep Curl: 3 set × 10 tekrar @ 55 kg",           "49.5", "55 kg"),

            // Tricep Pushdown — baseline 40 kg → safe max 44.0 kg
            ("Tricep Pushdown",        40.0,  "Tricep Pushdown: 3 set x 10 tekrar @ 50kg",       "44.0", "50kg"),
        };

        foreach (var c in cases)
            yield return new object[] { c.ExerciseName, c.BaselineKg, c.Input, c.ExpectedSafeWeight, c.OriginalToken };
    }

    [Fact]
    public void UnsafeOverloadCases_ContainsExactlyThirtyCases()
    {
        Assert.Equal(30, UnsafeOverloadCases().Count());
    }

    [Theory]
    [MemberData(nameof(UnsafeOverloadCases))]
    public void RecognizedExerciseWithBaseline_UnsafeRecommendation_IsCappedToTenPercentMax(
        string exerciseName,
        double baselineKg,
        string input,
        string expectedSafeWeight,
        string originalToken)
    {
        var context = BuildContextFor(exerciseName, baselineKg);

        var result = _sut.Validate(input, context);

        Assert.True(result.Triggered);
        Assert.DoesNotContain(originalToken, result.SanitizedReply);
        Assert.Contains($"{expectedSafeWeight} kg", result.SanitizedReply);
        Assert.Single(result.InterceptedProgressions);
        Assert.Contains(exerciseName, result.InterceptedProgressions[0]);
    }
}
