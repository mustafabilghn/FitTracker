using System.Collections.Generic;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// Chat metin guardrail'i (AcsmGuardrailService.Validate), serbest metinde güvenli sınırı aşan bir değeri
/// ÖNERMEK yerine onu sınırı aştığı için UYARI olarak anan bir ifadeyi körlemesine yeniden yazmamalı:
/// "115 kg … %110 sınırını aşıyor" → "110.0 kg … %110 sınırını aşıyor" kendi içinde çelişkili bir cümle üretir
/// (production smoke, write_unsafe_bench). Güvenlik zayıflamamalı: öneriler hâlâ kısılır.
/// </summary>
public class AcsmGuardrailWarningContextTests
{
    private readonly AcsmGuardrailService _sut = new();

    // Bench Press baseline 100 kg → güvenli sınır 110 kg.
    private static FitBotContextDto Context() => new()
    {
        TotalWorkouts = 5,
        WeightTrends = new List<ExerciseWeightTrendDto>
        {
            new()
            {
                ExerciseName = "Bench Press",
                Trend = "UP",
                WeeklyMaxWeights = new List<WeeklyMaxWeightDto> { new() { WeeksAgo = 0, MaxKg = 100 }, new() { WeeksAgo = 1, MaxKg = 95 } }
            }
        }
    };

    // ── Regresyon: uyarı cümlesi çelişkili hale getirilmemeli ──

    [Fact]
    public void WarningThatAValueExceedsTheLimit_IsNotRewrittenIntoAContradiction_SmokeCase()
    {
        // Production smoke'taki modelin (guardrail öncesi) cevabının yeniden kurgusu.
        const string reply =
            "Bench Press için 115 kg önerisi, son maks ağırlığın 100 kg olduğu göz önüne alındığında %110 sınırını aşıyor; " +
            "güvenli bir plan oluşturmak istersen ağırlığı 110 kg ya da daha düşük bir değere ayarlamanı öneriyorum.";

        var result = _sut.Validate(reply, Context());

        Assert.Equal(reply, result.SanitizedReply);
        Assert.DoesNotContain("110.0 kg önerisi", result.SanitizedReply); // "110 kg sınırı aşıyor" çelişkisi yok
        Assert.False(result.Triggered);
        Assert.Empty(result.InterceptedProgressions);
    }

    [Fact]
    public void EnglishWarningThatAValueExceedsTheLimit_IsNotRewritten()
    {
        const string reply = "The suggested 115 kg for Bench Press exceeds the safe progression limit, so keep it at 110 kg or lower.";

        var result = _sut.Validate(reply, Context());

        Assert.Equal(reply, result.SanitizedReply);
        Assert.False(result.Triggered);
    }

    // ── Güvenlik zayıflamamalı: öneriler hâlâ kısılır ──

    [Fact]
    public void FreeFormRecommendation_IsStillCapped()
    {
        var result = _sut.Validate("Bugün Bench Press 125 kg dene.", Context());

        Assert.True(result.Triggered);
        Assert.Equal("Bugün Bench Press 110.0 kg dene.", result.SanitizedReply);
    }

    [Fact]
    public void ExceedingAPersonalRecord_WithoutASafetyLimitReference_IsStillCapped()
    {
        // "aşıyorsun" (rekorunu aşıyorsun) bir güvenlik uyarısı değildir.
        var result = _sut.Validate("Bench Press 125 kg ile önceki rekorunu aşıyorsun, devam et.", Context());

        Assert.True(result.Triggered);
        Assert.Contains("110.0 kg", result.SanitizedReply);
        Assert.DoesNotContain("125", result.SanitizedReply);
    }

    [Fact]
    public void ExceedsVerbWithoutASafetyLimitReference_IsStillCapped()
    {
        // "aşıyor" eşleşir ama cümlecikte güvenlik sınırı ifadesi yok → uyarı değil, kısılır.
        var result = _sut.Validate("Bench Press 125 kg ile önceki rekorunu aşıyor.", Context());

        Assert.True(result.Triggered);
        Assert.DoesNotContain("125", result.SanitizedReply);
    }

    [Fact]
    public void FalseClaimThatAValueDoesNotExceedTheLimit_IsStillCapped()
    {
        var result = _sut.Validate("Bench Press 125 kg güvenli sınırı aşmaz, rahatça dene.", Context());

        Assert.True(result.Triggered);
        Assert.DoesNotContain("125", result.SanitizedReply);
    }

    [Fact]
    public void RecommendationInAnotherClause_IsCappedEvenIfAWarningFollows()
    {
        // Uyarı muafiyeti yalnızca değerin geçtiği cümleciğe uygulanır; ';' ile ayrılmış bir öneri yine kısılır.
        var result = _sut.Validate("Bench Press 125 kg yap; bu değer %110 sınırını aşıyor olabilir.", Context());

        Assert.True(result.Triggered);
        Assert.StartsWith("Bench Press 110.0 kg yap;", result.SanitizedReply);
    }

    [Fact]
    public void StructuredSuggestionLine_IsAlwaysCapped_EvenWithWarningWords()
    {
        var result = _sut.Validate("Bench Press: 3 set × 5 tekrar @ 125 kg (sınırı aşıyor)", Context());

        Assert.True(result.Triggered);
        Assert.Contains("@ 110.0 kg", result.SanitizedReply);
    }
}
