using FitTrackr.API.RAG;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// RAG cevap doğrulaması (saf fonksiyonlar): cümle bölme, kaynağa göre sayısal grounding (kişisel veri / passage /
/// ACSM ile doğrulanmış kg), Markdown bütünlüğü ve kaynak atfı doğrulaması. Skorlar gerçek cosine ölçeğindedir.
/// </summary>
public class RagAnswerGroundingTests
{
    private static readonly RagGroundingPassage WarmupPassage = new(
        "Direnç antrenmanından önce ısınma",
        "Ağır setlerden önce kısa genel ısınma ve hedef harekete özgü hafif ısınma setleri performansı destekleyebilir; ısınma yorgunluk yaratmamalıdır.",
        "Ribeiro et al., Warm-up in Resistance Training and Strength Performance: Systematic Review (2021)",
        "https://example.org/warmup", "v1", 0.526);

    private static readonly RagGroundingPassage DeloadPassage = new(
        "Deload nedir?",
        "Deload, antrenman yükünün geçici olarak azaltıldığı planlı bir dönemdir; yorgunluğu azaltmak ve toparlanmayı desteklemek için kullanılır. Literatürde 4-6 haftalık aralıklar geçer.",
        "Bell et al., Integrating Deloading into Strength and Physique Sports Training: Delphi Consensus (2023)",
        "https://doi.org/10.1186/s40798-023-00633-0", "Sports Med Open 2023;9:87", 0.652);

    private static readonly RagGroundingPassage IrrelevantRestPassage = new(
        "Setler arası dinlenme süresi",
        "Setler arası dinlenme süresi kuvvet hedefinde bir sonraki setin performansını koruyacak kadar uzun tutulur.",
        "Grgic et al., Rest Interval Duration and Muscular Strength: Systematic Review (2018); ACSM Position Stand (2026)",
        "https://example.org/rest", "v1", 0.384);

    private static NumericGroundingEvidence Evidence(params RagGroundingPassage[] passages)
    {
        var passageNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in passages)
            foreach (System.Text.RegularExpressions.Match m in RagAnswerGrounding.NumericExpression.Matches(p.Text))
                passageNumbers.Add(RagGroundingContext.NormalizeNumber(m.Value));

        return new NumericGroundingEvidence
        {
            Passages = passages,
            IsPassageNumber = v => passageNumbers.Contains(RagGroundingContext.NormalizeNumber(v)),
            TrustedNumbers = new HashSet<string> { "100", "120", "95" }, // kullanıcının kendi verisi
            ExerciseBaselines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Bench Press"] = 100, ["Squat"] = 120 }
        };
    }

    // ───────────────────── Sentence splitting ─────────────────────

    [Theory]
    [InlineData("Hafif kardiyo (ör. yürüyüş, bisiklet) yap. Sonra esne.", 2)]
    [InlineData("Ör. yürüyüş yap. Sonra esne.", 2)]
    [InlineData("Dinamik hareketler vb. ekleyebilirsin. Sonra başla.", 2)]
    [InlineData("Bar 2.5 kg artırılabilir. Sonra dinlen.", 2)]
    [InlineData("Grgic et al. 2018 bunu inceledi. Sonuç net.", 2)]
    [InlineData("Use light sets, e.g. empty bar first. Then load up.", 2)]
    [InlineData("Warm up with cardio (approx. five minutes) first. Then lift.", 2)]
    [InlineData("Tek cümle", 1)]
    public void SplitSentences_RespectsTurkishAndEnglishAbbreviations_DecimalsAndParentheses(string text, int expected)
    {
        var sentences = RagAnswerGrounding.SplitSentences(text);
        Assert.Equal(expected, sentences.Count);
        Assert.Equal(text, string.Concat(sentences)); // birleştirince metin birebir aynı
    }

    // ───────────────────── Numeric grounding ─────────────────────

    [Fact]
    public void AcsmValidatedPersonalWeightRecommendation_IsKept()
    {
        // mixed_bench_next_step: baseline 100 → güvenli sınır 110. 105 kg backend'in ACSM kuralıyla doğrulanır.
        var text = "Bench Press'te gelecek hafta 105 kg deneyebilirsin. Tekniği koru.";

        Assert.Equal(text, RagAnswerGrounding.ApplyNumericGrounding(text, Evidence(DeloadPassage), out var changed));
        Assert.False(changed);
    }

    [Fact]
    public void ValidatedWeight_DoesNotLaunderAnUnverifiedNumberInTheSameSentence()
    {
        // "5 tekrar" ne kullanıcı verisinde ne kaynakta: doğrulanmış kg aynı cümledeki doğrulanmamış iddiayı aklamaz.
        var text = "Bench Press'te gelecek hafta 105 kg ile 5 tekrar deneyebilirsin. Tekniği koru.";

        Assert.Equal("Tekniği koru.", RagAnswerGrounding.ApplyNumericGrounding(text, Evidence(DeloadPassage), out _));
    }

    [Fact]
    public void AcsmValidatedWeight_WithBackedRepCount_IsKeptWhole()
    {
        var evidence = Evidence(DeloadPassage);
        evidence = new NumericGroundingEvidence
        {
            Passages = evidence.Passages, IsPassageNumber = evidence.IsPassageNumber,
            TrustedNumbers = new HashSet<string>(evidence.TrustedNumbers) { "5" }, ExerciseBaselines = evidence.ExerciseBaselines
        };
        var text = "Bench Press'te gelecek hafta 110 kg ile 5 tekrar deneyebilirsin.";

        Assert.Equal(text, RagAnswerGrounding.ApplyNumericGrounding(text, evidence, out var changed));
        Assert.False(changed);
    }

    [Theory]
    [InlineData("Bench Press'te 125 kg dene. Formuna dikkat et.")]          // baseline 100 → %25 artış: güvensiz
    [InlineData("Squat için 150 kg yükle. Formuna dikkat et.")]              // baseline 120 → %25: güvensiz
    [InlineData("Bu hafta 105 kg kullan. Formuna dikkat et.")]               // egzersiz adı yok → doğrulanamaz
    [InlineData("Deadlift'te 105 kg dene. Formuna dikkat et.")]              // baseline'ı olmayan egzersiz
    public void UnverifiedOrUnsafeWeight_IsRemoved_RestOfAnswerKept(string text)
    {
        var result = RagAnswerGrounding.ApplyNumericGrounding(text, Evidence(DeloadPassage), out _);

        Assert.Equal("Formuna dikkat et.", result);
    }

    [Fact]
    public void PersonalAndPassageNumbersAndAcsmRule_AreKept()
    {
        var text = "Son Bench Press maksimumun 100 kg. Literatürde 4-6 haftalık aralıklar geçer. Artışı %10 sınırında tut.";

        Assert.Equal(text, RagAnswerGrounding.ApplyNumericGrounding(text, Evidence(DeloadPassage), out var changed));
        Assert.False(changed);
    }

    [Fact]
    public void UnsupportedNumberInsideParenthesis_RemovesOnlyTheParenthesis()
    {
        var text = "Deload haftasında hacmi azalt (yaklaşık %40 kadar) ve toparlanmaya odaklan.";

        var result = RagAnswerGrounding.ApplyNumericGrounding(text, Evidence(DeloadPassage), out _);

        Assert.Equal("Deload haftasında hacmi azalt ve toparlanmaya odaklan.", result);
    }

    [Fact]
    public void WarmupAnswer_DoesNotStartMidSentence_AfterAbbreviation()
    {
        // Önceki hata: "ör." cümle sanılıp bölünüyor, cevap "yürüyüş, bisiklet) yaparak…" diye başlıyordu.
        var text = "Önce 5-10 dakika hafif aerobik aktivite (ör. yürüyüş, bisiklet) yaparak vücudunu ısıtabilirsin. " +
                   "Ardından hedef hareketle hafif ısınma setleri yap.";

        var result = RagAnswerGrounding.ApplyNumericGrounding(text, Evidence(WarmupPassage), out _);

        Assert.Equal("Ardından hedef hareketle hafif ısınma setleri yap.", result);
        Assert.DoesNotContain("bisiklet)", result);
    }

    [Fact]
    public void Markdown_ListStructurePreserved_UnsupportedBulletDroppedWholly_NoDanglingBold()
    {
        var text = "Squat'ta ilerlemek için şunları deneyebilirsin:\n\n" +
                   "- **Tempo**: İnişi 3 saniyeye uzat.\n" +
                   "- **Varyasyon**: Pause Squat ekle.\n" +
                   "- **Hacim**: Haftada 12 set yap. Tekniği koru.";

        var result = RagAnswerGrounding.ApplyNumericGrounding(text, Evidence(DeloadPassage), out var changed);

        Assert.True(changed);
        Assert.StartsWith("Squat'ta ilerlemek için şunları deneyebilirsin:", result);
        Assert.Contains("- **Varyasyon**: Pause Squat ekle.", result);
        Assert.DoesNotContain("3 saniye", result);
        Assert.DoesNotContain("12 set", result);
        Assert.DoesNotContain("- **Tempo**", result);                   // tamamen kaynaksız madde satırı düşürüldü
        Assert.Contains("- **Hacim**: Tekniği koru.", result);           // satırın geri kalanı korundu
        foreach (var line in result.Split('\n'))
            Assert.True(System.Text.RegularExpressions.Regex.Matches(line, @"\*\*").Count % 2 == 0, $"dangling bold: {line}");
    }

    [Fact]
    public void FixedScheduleCaveat_IsStillEnforced()
    {
        var passage = DeloadPassage with { Text = DeloadPassage.Text + " Bell paneli sabit bir aralık önermedi." };

        var result = RagAnswerGrounding.ApplyNumericGrounding("Her 4-6 haftada bir deload yapmalısın. Yorgunluğu izle.", Evidence(passage), out _);

        Assert.Equal("Yorgunluğu izle.", result);
    }

    [Fact]
    public void NoPassages_TextIsUntouched()
    {
        var text = "Haftada 3 gün antrenman yap.";
        Assert.Equal(text, RagAnswerGrounding.ApplyNumericGrounding(text, Evidence(), out var changed));
        Assert.False(changed);
    }

    // ───────────────────── Citations ─────────────────────

    [Fact]
    public void FabricatedCitation_WithOnlyIrrelevantRetrieval_IsRemoved()
    {
        // know_squat_technique: getirilen chunk'lar ilgisiz (0,384) ama cevap "ACSM 2026" diye kaynak gösteriyordu.
        var text = "Çömelirken dizlerini ayak parmaklarının hizasında tut ve kalçanı geriye it.\nKaynak: ACSM Position Stand (2026)";

        var result = RagAnswerGrounding.VerifyCitations(text, new[] { IrrelevantRestPassage });

        Assert.Equal("Çömelirken dizlerini ayak parmaklarının hizasında tut ve kalçanı geriye it.", result);
    }

    [Fact]
    public void SupportedCitation_FromRelevantMatchingPassage_IsKept()
    {
        var text = "Deload, antrenman yükünün geçici olarak azaltıldığı planlı bir dönemdir ve toparlanmayı destekler.\nKaynak: Bell et al. (2023)";

        var result = RagAnswerGrounding.VerifyCitations(text, new[] { DeloadPassage });

        Assert.Equal(text, result);
    }

    [Fact]
    public void CitationOfSourceThatWasNotRetrieved_IsRemoved_EvenWhenRetrievalIsRelevant()
    {
        var text = "Deload, antrenman yükünün geçici olarak azaltıldığı planlı bir dönemdir ve toparlanmayı destekler.\nKaynak: NSCA Essentials (2016)";

        var result = RagAnswerGrounding.VerifyCitations(text, new[] { DeloadPassage });

        Assert.DoesNotContain("NSCA", result);
        Assert.DoesNotContain("Kaynak", result);
    }

    [Fact]
    public void MixedCitationList_KeepsOnlySupportedParts()
    {
        var text = "Deload, antrenman yükünün geçici olarak azaltıldığı planlı bir dönemdir ve toparlanmayı destekler.\nKaynak: Bell et al. (2023); WHO Guidelines (2020)";

        var result = RagAnswerGrounding.VerifyCitations(text, new[] { DeloadPassage });

        Assert.EndsWith("Kaynak: Bell et al. (2023)", result);
        Assert.DoesNotContain("WHO", result);
    }

    [Fact]
    public void ParentheticalAcademicCitation_IsValidated_NonCitationParenthesisKept()
    {
        var text = "Deload yükün geçici olarak azaltıldığı planlı bir dönemdir (Bell et al., 2023; Grgic et al., 2018). " +
                   "Toparlanmayı destekler (2023 güncellemesi).";

        var result = RagAnswerGrounding.VerifyCitations(text, new[] { DeloadPassage, IrrelevantRestPassage });

        Assert.Contains("(Bell et al., 2023)", result);        // ilgili ve eşleşen passage
        Assert.DoesNotContain("Grgic", result);                // ilgisiz (0,384) passage kaynak olarak gösterilmez
        Assert.Contains("(2023 güncellemesi)", result);        // akademik atıf değil, dokunulmaz
    }

    [Fact]
    public void InternalModelMarkup_IsStripped()
    {
        var text = "Aşamalı yüklenme uyaranı kademeli artırmaktır【sourceName: ACSM Position Stand (2026)】. Tekniği koru【Bell et al.】.";

        var result = RagAnswerGrounding.StripInternalMarkup(text);

        Assert.Equal("Aşamalı yüklenme uyaranı kademeli artırmaktır. Tekniği koru.", result);
        Assert.DoesNotContain("【", result);
        Assert.DoesNotContain("sourceName", result);
    }

    [Fact]
    public void NoRetrieval_RemovesAllSourceLinesAndAcademicCitations()
    {
        var text = "Isınma için hafif setler yap (Ribeiro et al., 2021).\nSources: ACSM 2026";

        var result = RagAnswerGrounding.RemoveAllCitations(text);

        Assert.Equal("Isınma için hafif setler yap.", result);
    }
}
