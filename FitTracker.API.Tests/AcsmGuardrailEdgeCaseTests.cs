// DRAFT — henüz repoya eklenmedi. Gözden geçirip uygun bulursan
// FitTracker.API.Tests/ klasörüne kopyalayıp `dotnet test` ile çalıştır.
//
// Bu dosya, mevcut AcsmGuardrailServiceTests.cs'e EK olarak tasarlandı;
// var olan testlere veya production koduna dokunmuyor.
//
// Her test, AcsmGuardrailService.cs'in regex/eşleştirme mantığı okunarak
// yazıldı (SuggestionLinePattern + InlineWeightPattern + TryMatchExercise).
// Yorumlar, koddan çıkarılan BEKLENEN davranışı açıklıyor — ben burada
// derleyip çalıştıramadım (sandbox'ta dotnet SDK yok, canlı API'ne de
// erişemiyorum), o yüzden "beklenen" ile "gerçek" sonucu SEN doğrulamalısın.
// Bir test beklenenin tersini gösterirse, bu kötü bir şey değil — tam da
// makalenin "guardrail'in kapsamı sınırlı" dediği yeri somut sayıya
// döker, ve dürüstçe raporlanırsa makalenin integrity'sini güçlendirir.

using System.Collections.Generic;
using System.Linq;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services;
using Xunit;

namespace FitTracker.API.Tests;

public class AcsmGuardrailEdgeCaseTests
{
    private readonly AcsmGuardrailService _sut = new();

    // Bench Press baseline: 100 kg → safe-max 110 kg
    // Squat       baseline: 120 kg → safe-max 132 kg
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
                }
            },
            new()
            {
                ExerciseName = "Squat",
                Trend = "artıyor",
                WeeklyMaxWeights = new List<WeeklyMaxWeightDto>
                {
                    new() { WeeksAgo = 0, MaxKg = 120.0 },
                }
            }
        },
        RecentWorkouts = new List<WorkoutContextEntryDto>()
    };

    // ─────────────────────────────────────────────────────────────────
    // GAP 1 — Pound (lb) unit is never recognized.
    // Both regexes hard-code the literal "kg" — a recommendation given
    // entirely in pounds contains no "kg" substring at all, so neither
    // SuggestionLinePattern nor InlineWeightPattern can match it.
    // EXPECTED (from reading the code): NOT intercepted, even though
    // 121 kg ≈ 267 lb is far above the 110 kg safe-max.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Gap1_PoundUnit_IsNotRecognized()
    {
        const string input = "Bench Press: 3 set x 10 tekrar @ 267 lb";
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Equal(input, result.SanitizedReply);
    }

    // ─────────────────────────────────────────────────────────────────
    // GAP 2 — Verbal / percentage recommendations with no explicit
    // kg number bypass the guardrail entirely (no digit+kg to match).
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Gap2_PercentageWording_IsNotRecognized()
    {
        const string input = "Bench Press için ağırlığını %20 artırmayı deneyebilirsin.";
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Equal(input, result.SanitizedReply);
    }

    // ─────────────────────────────────────────────────────────────────
    // GAP 3 — Range format ("100-125 kg arası"): InlineWeightPattern
    // only anchors on "<digits> kg", so in a hyphenated range only the
    // number immediately before "kg" is captured (125), the lower bound
    // (100) is invisible to the regex. Expected: triggers on 125 only.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Gap3_RangeFormat_OnlyUpperBoundIsChecked()
    {
        const string input = "Bugün Bench Press için 100-125 kg arası bir ağırlık dene.";
        var result = _sut.Validate(input, BuildContext());

        Assert.True(result.Triggered);
        Assert.Contains("110.0 kg", result.SanitizedReply);
        // "100-" prefix is expected to remain untouched in the output.
    }

    // ─────────────────────────────────────────────────────────────────
    // GAP 4 — Multi-exercise, same line, two different baselines.
    // TryMatchExercise returns the FIRST matching exercise found via
    // dictionary iteration, then InlineWeightPattern.Replace applies
    // globally to EVERY "<digits> kg" on that line using only that one
    // exercise's safe-max. Expected: Squat's 140 kg is incorrectly
    // evaluated against Bench Press's 110 kg safe-max (or vice versa,
    // depending on dictionary iteration order), producing either a
    // false trigger or a missed unsafe value for the second exercise.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Gap4_TwoExercisesSameLine_SecondExerciseUsesWrongBaseline()
    {
        const string input =
            "Bugün Bench Press 105 kg ve Squat 140 kg deneyebilirsin.";
        var result = _sut.Validate(input, BuildContext());

        // Squat's own safe-max is 132 kg, so 140 kg SHOULD be capped to 132.0 kg
        // if evaluated against its own baseline. If the guardrail instead
        // reuses Bench Press's 110 kg safe-max for both numbers on the line,
        // this assertion will fail — which is the point of this test.
        Assert.Contains("132.0 kg", result.SanitizedReply);
    }

    // ─────────────────────────────────────────────────────────────────
    // GAP 5 — Exercise alias not present in the baseline dictionary.
    // TryGetBaseline/TryMatchExercise only do case-insensitive substring
    // matching against whatever name is already stored (e.g. "Bench
    // Press"). A different-language or abbreviated alias with no
    // substring relationship ("Yatarak Pres") will not match at all.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Gap5_UnmappedTurkishAlias_IsNotRecognized()
    {
        const string input = "Bugün Yatarak Pres için 121 kg deneyebilirsin.";
        var result = _sut.Validate(input, BuildContext());

        Assert.False(result.Triggered);
        Assert.Equal(input, result.SanitizedReply);
    }
}
