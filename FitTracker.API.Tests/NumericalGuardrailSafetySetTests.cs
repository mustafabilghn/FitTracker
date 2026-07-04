using System.Collections.Generic;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services;
using Xunit;

namespace FitTrackr.API.Tests;

/// <summary>
/// Constructed numerical overload test set for the post-generation load guardrail.
/// Each case uses a recognized exercise name, an available baseline, and a supported
/// recommendation format. The suite intentionally measures only the documented
/// scope of the guardrail; it is not a clinical safety or injury-prevention benchmark.
/// </summary>
public class NumericalGuardrailSafetySetTests
{
    private readonly AcsmGuardrailService _sut = new();

    public static IEnumerable<object[]> UnsafeRecognizedFormatCases()
    {
        yield return Case("Bench Press", 100.0,
            "Bench Press: 3 set × 10 tekrar @ 121 kg",
            "Bench Press: 3 set × 10 tekrar @ 110.0 kg");
        yield return Case("Bench Press", 100.0,
            "bench press: 4 set x 8 tekrar @ 125 kg",
            "bench press: 4 set x 8 tekrar @ 110.0 kg");
        yield return Case("Bench Press", 100.0,
            "Bench Press: 5 set × 5 tekrar @ 121,5 kg",
            "Bench Press: 5 set × 5 tekrar @ 110.0 kg");
        yield return Case("Bench Press", 100.0,
            "Bench Press: 3 set x 10 tekrar @ 121kg",
            "Bench Press: 3 set x 10 tekrar @ 110.0 kg");
        yield return Case("Bench Press", 100.0,
            "Bench Press: 3 set x 10 tekrar @ 121  kg",
            "Bench Press: 3 set x 10 tekrar @ 110.0 kg");

        yield return Case("Squat", 120.0,
            "Squat: 3 set × 8 tekrar @ 133 kg",
            "Squat: 3 set × 8 tekrar @ 132.0 kg");
        yield return Case("Squat", 120.0,
            "squat: 5 set x 5 tekrar @ 150 kg",
            "squat: 5 set x 5 tekrar @ 132.0 kg");
        yield return Case("Squat", 120.0,
            "Squat: 4 set × 6 tekrar @ 132,5 kg",
            "Squat: 4 set × 6 tekrar @ 132.0 kg");
        yield return Case("Squat", 120.0,
            "Squat: 3 set x 10 tekrar @ 133kg",
            "Squat: 3 set x 10 tekrar @ 132.0 kg");
        yield return Case("Squat", 120.0,
            "Squat: 3 set x 10 tekrar @ 140  kg",
            "Squat: 3 set x 10 tekrar @ 132.0 kg");

        yield return Case("Deadlift", 140.0,
            "Deadlift: 3 set × 5 tekrar @ 155 kg",
            "Deadlift: 3 set × 5 tekrar @ 154.0 kg");
        yield return Case("Deadlift", 140.0,
            "Deadlift: 1 set x 3 tekrar @ 200 kg",
            "Deadlift: 1 set x 3 tekrar @ 154.0 kg");
        yield return Case("Deadlift", 140.0,
            "deadlift: 4 set × 4 tekrar @ 154,5 kg",
            "deadlift: 4 set × 4 tekrar @ 154.0 kg");
        yield return Case("Deadlift", 140.0,
            "Deadlift: 2 set x 5 tekrar @ 155kg",
            "Deadlift: 2 set x 5 tekrar @ 154.0 kg");
        yield return Case("Deadlift", 140.0,
            "Deadlift: 2 set x 5 tekrar @ 180  kg",
            "Deadlift: 2 set x 5 tekrar @ 154.0 kg");

        yield return Case("Overhead Press", 60.0,
            "Overhead Press: 3 set × 8 tekrar @ 67 kg",
            "Overhead Press: 3 set × 8 tekrar @ 66.0 kg");
        yield return Case("Barbell Row", 80.0,
            "Barbell Row: 4 set x 10 tekrar @ 89 kg",
            "Barbell Row: 4 set x 10 tekrar @ 88.0 kg");
        yield return Case("Leg Press", 200.0,
            "Leg Press: 3 set × 12 tekrar @ 221 kg",
            "Leg Press: 3 set × 12 tekrar @ 220.0 kg");
        yield return Case("Incline Dumbbell Press", 70.0,
            "Incline Dumbbell Press: 4 set x 10 tekrar @ 78 kg",
            "Incline Dumbbell Press: 4 set x 10 tekrar @ 77.0 kg");
        yield return Case("Lat Pulldown", 65.0,
            "Lat Pulldown: 3 set × 12 tekrar @ 72 kg",
            "Lat Pulldown: 3 set × 12 tekrar @ 71.5 kg");

        yield return Case("Cable Row", 75.0,
            "Cable Row: 4 set x 10 tekrar @ 83 kg",
            "Cable Row: 4 set x 10 tekrar @ 82.5 kg");
        yield return Case("Dumbbell Shoulder Press", 30.0,
            "Dumbbell Shoulder Press: 3 set × 10 tekrar @ 34 kg",
            "Dumbbell Shoulder Press: 3 set × 10 tekrar @ 33.0 kg");
        yield return Case("Hip Thrust", 120.0,
            "Hip Thrust: 4 set x 8 tekrar @ 133 kg",
            "Hip Thrust: 4 set x 8 tekrar @ 132.0 kg");
        yield return Case("Bicep Curl", 20.0,
            "Bicep Curl: 3 set × 12 tekrar @ 23 kg",
            "Bicep Curl: 3 set × 12 tekrar @ 22.0 kg");
        yield return Case("Tricep Pushdown", 40.0,
            "Tricep Pushdown: 3 set x 12 tekrar @ 45 kg",
            "Tricep Pushdown: 3 set x 12 tekrar @ 44.0 kg");

        yield return Case("Bench Press", 100.0,
            "Bench Press: 6 set × 3 tekrar @ 150 kg",
            "Bench Press: 6 set × 3 tekrar @ 110.0 kg");
        yield return Case("Squat", 120.0,
            "Squat: 5 set x 3 tekrar @ 147 kg",
            "Squat: 5 set x 3 tekrar @ 132.0 kg");
        yield return Case("Deadlift", 140.0,
            "Deadlift: 3 set × 3 tekrar @ 154,1 kg",
            "Deadlift: 3 set × 3 tekrar @ 154.0 kg");
        yield return Case("Leg Press", 200.0,
            "Leg Press: 4 set x 15 tekrar @ 250 kg",
            "Leg Press: 4 set x 15 tekrar @ 220.0 kg");
        yield return Case("Lat Pulldown", 65.0,
            "Lat Pulldown: 3 set × 10 tekrar @ 90 kg",
            "Lat Pulldown: 3 set × 10 tekrar @ 71.5 kg");
    }

    [Theory]
    [MemberData(nameof(UnsafeRecognizedFormatCases))]
    public void ConstructedUnsafeRecommendation_IsInterceptedAndCapped(
        string exerciseName,
        double baselineKg,
        string input,
        string expectedReply)
    {
        var result = _sut.Validate(input, BuildContext(exerciseName, baselineKg));

        Assert.True(result.Triggered);
        Assert.Equal(expectedReply, result.SanitizedReply);
        Assert.Single(result.InterceptedProgressions);
        Assert.Contains(exerciseName, result.InterceptedProgressions[0]);
    }

    private static object[] Case(string exerciseName, double baselineKg, string input, string expectedReply) =>
        new object[] { exerciseName, baselineKg, input, expectedReply };

    private static FitBotContextDto BuildContext(string exerciseName, double baselineKg) => new()
    {
        TotalWorkouts = 10,
        WeightTrends = new List<ExerciseWeightTrendDto>
        {
            new()
            {
                ExerciseName = exerciseName,
                Trend = "UP",
                WeeklyMaxWeights = new List<WeeklyMaxWeightDto>
                {
                    new() { WeeksAgo = 0, MaxKg = baselineKg },
                    new() { WeeksAgo = 1, MaxKg = baselineKg - 2.5 }
                }
            }
        },
        RecentWorkouts = new List<WorkoutContextEntryDto>()
    };
}
