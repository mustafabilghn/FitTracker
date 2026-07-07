using System.Collections.Generic;
using System.Linq;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// Unit tests for the recognized structured-recommendation scope of AcsmGuardrailService.Validate().
/// </summary>
public class AcsmGuardrailServiceTests
{
    private readonly AcsmGuardrailService _sut = new();

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

    [Fact]
    public void UnsafeStructuredSuggestion_IsInterceptedAndCapped()
    {
        const string input = "Bench Press: 3 set × 10 tekrar @ 121 kg";
        var result = _sut.Validate(input, BuildContext());

        Assert.True(result.Triggered);
        Assert.DoesNotContain("121 kg", result.SanitizedReply);
        Assert.Contains("110.0 kg", result.SanitizedReply);
        Assert.True(result.InterceptedProgressions.Any(s => s.Contains("Bench Press")));
    }

    [Fact]
    public void ExactlyTenPercentStructuredSuggestion_IsNotIntercepted()
    {
        const string input = "Bench Press: 3 set × 10 tekrar @ 110 kg";
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Equal(input, result.SanitizedReply);
    }

    [Fact]
    public void SafeStructuredSuggestion_PassesThroughUnchanged()
    {
        const string input = "Bench Press: 3 set × 10 tekrar @ 105 kg";
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Equal(input, result.SanitizedReply);
    }

    [Fact]
    public void MultipleStructuredSuggestions_OnlyUnsafeOneIsIntercepted()
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

    [Fact]
    public void StructuredSuggestionWithoutBaseline_PassesThroughUnchanged()
    {
        const string input = "Cable Fly: 3 set × 12 tekrar @ 150 kg";
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Equal(input, result.SanitizedReply);
    }

    [Theory]
    [InlineData("Bench Press: 3 set x 10 tekrar @ 121kg")]
    [InlineData("Bench Press: 3 set x 10 tekrar @ 121  kg")]
    [InlineData("Bench Press: 3 set x 10 tekrar @ 121,5 kg")]
    public void SupportedStructuredFormattingVariants_AreInterceptedAndCapped(string input)
    {
        var result = _sut.Validate(input, BuildContext());

        Assert.True(result.Triggered);
        Assert.DoesNotContain("121", result.SanitizedReply);
        Assert.Contains("110.0 kg", result.SanitizedReply);
    }

    [Fact]
    public void FreeTextRecommendation_IsOutsideStructuredScopeAndPassesThrough()
    {
        const string input = "Bugün Bench Press için 121 kg deneyebilirsin.";
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Equal(input, result.SanitizedReply);
    }
}
