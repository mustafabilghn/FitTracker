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

    // ── Ret/uyarı bağlamları (TR + EN): değer olduğu gibi kalmalı, çelişki oluşmamalı ──

    [Theory]
    // Production smoke (2. tekrar): "…%110'undan yüksek olduğu için … kabul edilmiyor"
    [InlineData("Bench Press için 115 kg, 3 set × 5 tekrar isteğin, son kaydedilen maksimum ağırlığın %110'undan yüksek olduğu için sistem tarafından kabul edilmiyor.")]
    // Production smoke (3. tekrar): "…yüksek olduğu için sistem bu planı kaydetmeyi onaylamıyor"
    [InlineData("İstediğin 115 kg, Bench Press için son kaydedilen maksimum ağırlığın %110’undan (110 kg) yüksek olduğu için sistem bu planı kaydetmeyi onaylamıyor.")]
    [InlineData("Bench Press için 115 kg güvenli sınırın üzerinde olduğu için plan kaydedilemez.")]
    [InlineData("For Bench Press, 115 kg is above the safe limit, so the plan is not approved.")]
    [InlineData("Bench Press için 115 kg limitin üzerinde olduğu için izin verilmiyor.")]
    [InlineData("Bench Press için 115 kg güvenli sınırı aşıyor.")]
    [InlineData("Bench Press için 115 kg güvenli sınırın üzerinde, bu nedenle uygun değil.")]
    [InlineData("Bench Press 115 kg %110 kuralına göre reddedildi.")]
    [InlineData("Bench Press için 115.5 kg güvenli sınırı aştığı için kabul edilmiyor.")] // ondalık nokta cümle sonu değil
    [InlineData("For Bench Press, 115 kg is higher than the 110% safety limit and is rejected.")]
    [InlineData("For Bench Press, 115 kg is above the safe limit and is not allowed.")]
    [InlineData("For Bench Press, 115 kg exceeds the safe limit.")]
    [InlineData("For Bench Press, 115 kg is over the safe limit, therefore not allowed.")]
    public void WarningOrRefusalAboutTheSafetyLimit_IsLeftAsWritten(string reply)
    {
        var result = _sut.Validate(reply, Context());

        Assert.Equal(reply, result.SanitizedReply);
        Assert.DoesNotContain("110.0 kg", result.SanitizedReply);
        Assert.False(result.Triggered);
    }

    // ── Öneriler (güvenlik sınırı kelimeleri geçse bile) hâlâ kısılmalı ──

    [Theory]
    [InlineData("Bench Press 115 kg yap.")]
    [InlineData("Bench Press için 115 kg öneriyorum.")]
    [InlineData("Bench Press'i 115 kg'a çıkar.")]
    [InlineData("Bench Press 115 kg ile 3x5 yap.")]
    [InlineData("Bench Press 115 kg biraz yüksek ama dene.")]                          // güvenlik sınırı yok
    [InlineData("Bench Press 115 kg senin için yüksek, uygun değil.")]                // ret var ama güvenlik sınırı yok
    [InlineData("Bench Press 115 kg yap, %110 sınırını aşıyor ama sorun değil.")]      // aynı cümlecikte emir → öneri
    [InlineData("Bench Press için 115 kg öneriyorum, güvenli sınırın üzerinde olsa da kabul edilmiyor değil.")]
    [InlineData("I recommend 115 kg for Bench Press even though it exceeds the safe limit.")]
    [InlineData("Try 115 kg on Bench Press; it is fine.")]
    [InlineData("Go for 115 kg on Bench Press, above the safe limit but not allowed to stop you.")]
    [InlineData("Bench Press'i 115 kg'a çıkar, güvenli sınırın üzerinde ama sistem onaylamıyor diye bırakma.")]
    public void RecommendationsAreStillCapped_EvenWithLimitOrRefusalWords(string reply)
    {
        var result = _sut.Validate(reply, Context());

        Assert.True(result.Triggered);
        Assert.Contains("110.0 kg", result.SanitizedReply);
        Assert.DoesNotContain("115", result.SanitizedReply);
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
