using System.Collections.Generic;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// Regression tests for numeric text that must remain untouched because it is not a
/// recognized structured exercise-load recommendation.
/// </summary>
public class GuardrailScopePreservationTests
{
    private readonly AcsmGuardrailService _sut = new();

    public static IEnumerable<object[]> NonRecommendationOrOutOfScopeNumericCases()
    {
        yield return new object[] { "Geçen hafta Bench Press'te 125 kg yaptın." };
        yield return new object[] { "Bench Press öncesi vücut ağırlığın 125 kg olabilir." };
        yield return new object[] { "Bench Press için son kaydın 125 kg." };
        yield return new object[] { "Bench Press'teki kişisel rekorun 125 kg olarak görünüyor." };
        yield return new object[] { "Bench Press: geçen hafta 125 kg yaptın." };
        yield return new object[] { "Bugün Bench Press için 121 kg deneyebilirsin." };
        yield return new object[] { "Bugün toplam 2500 kcal almayı hedefleyebilirsin." };
        yield return new object[] { "Son antrenmanını 07.07.2026 tarihinde yaptın." };
        yield return new object[] { "Bench Press'te 3 set boyunca 12 tekrar yaptın." };
        yield return new object[] { "Bench Press: 3 set x 10 tekrar @ 121 lbs" };
    }

    [Theory]
    [MemberData(nameof(NonRecommendationOrOutOfScopeNumericCases))]
    public void NonRecommendationOrOutOfScopeNumericText_IsPreserved(string input)
    {
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Equal(input, result.SanitizedReply);
        Assert.Empty(result.InterceptedProgressions);
    }

    private static FitBotContextDto BuildContext() => new()
    {
        TotalWorkouts = 10,
        WeightTrends = new List<ExerciseWeightTrendDto>
        {
            new()
            {
                ExerciseName = "Bench Press",
                Trend = "UP",
                WeeklyMaxWeights = new List<WeeklyMaxWeightDto>
                {
                    new() { WeeksAgo = 0, MaxKg = 100.0 },
                    new() { WeeksAgo = 1, MaxKg = 97.5 }
                }
            },
            new()
            {
                ExerciseName = "Squat",
                Trend = "UP",
                WeeklyMaxWeights = new List<WeeklyMaxWeightDto>
                {
                    new() { WeeksAgo = 0, MaxKg = 120.0 },
                    new() { WeeksAgo = 1, MaxKg = 117.5 }
                }
            }
        },
        RecentWorkouts = new List<WorkoutContextEntryDto>()
    };
}
