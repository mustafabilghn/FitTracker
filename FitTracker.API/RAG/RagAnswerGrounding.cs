using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FitTrackr.API.Services;

namespace FitTrackr.API.RAG
{
    /// <summary>Sayısal grounding için güvenilir sayı kaynakları (her biri ayrı bir doğrulama mekanizmasıdır).</summary>
    public sealed class NumericGroundingEvidence
    {
        /// <summary>Bu istekte gerçekten getirilen bilgi parçaları (yalnızca status=ok).</summary>
        public IReadOnlyList<RagGroundingPassage> Passages { get; init; } = Array.Empty<RagGroundingPassage>();

        /// <summary>Getirilen passage metinlerinde birebir geçen sayısal ifade mi?</summary>
        public Func<string, bool> IsPassageNumber { get; init; } = _ => false;

        /// <summary>
        /// Kullanıcının kendi verisinden gelen sayılar (normalize): sunucunun context'i, başarılı kişisel tool sonuçları ve
        /// kullanıcının kendi mesajı. Bu sayılar kullanıcı hakkında gerçektir; kaynaktan gelmeleri gerekmez.
        /// </summary>
        public IReadOnlySet<string> TrustedNumbers { get; init; } = new HashSet<string>();

        /// <summary>Egzersiz → baseline (kg). ACSM ≤%10 kuralıyla doğrulanan kilo önerileri için tek referans.</summary>
        public IReadOnlyDictionary<string, double> ExerciseBaselines { get; init; } = new Dictionary<string, double>();

        public bool English { get; init; }
    }

    /// <summary>
    /// RAG cevabının çıktı tarafı doğrulaması. Saf (stateless) fonksiyonlardır; <c>AiWorkoutCoachService</c> çağırır.
    ///
    /// 1) Sayısal grounding: her sayı bir doğrulama mekanizmasına bağlanmalıdır — (a) kullanıcının kendi verisi,
    ///    (b) getirilen passage'ta birebir geçen ifade, (c) kullanıcının baseline'ına göre ACSM ≤%10 kuralını sağlayan
    ///    "kg" önerisi (egzersiz aynı cümlede adlandırılmış olmalı), (d) uygulamanın kendi ACSM %10 kuralı.
    ///    Desteklenmeyen sayı önce yalnızca içinde bulunduğu parantezden, olmazsa yalnızca kendi cümlesinden çıkarılır;
    ///    paragraf/cevap bütün olarak silinmez. Markdown satır yapısı (liste/başlık) korunur, yarım kalan satır düşürülür.
    ///    ACSM guardrail bu adımdan SONRA ayrıca ve değişmeden çalışır.
    /// 2) Cümle bölme: Türkçe/İngilizce kısaltmalar (ör., vb., e.g., et al. …), ondalık sayılar ve parantez içleri bölünmez.
    /// 3) Kaynak atfı: yalnızca yeterince ilgili (skor ≥ <see cref="CitationMinScore"/>) VE cevap içeriğiyle örtüşen bir
    ///    passage'ın kaynak metadata'sıyla eşleşen atıflar kalır. Modelin dahili işaretlemeleri (【…】) her zaman kaldırılır.
    /// </summary>
    public static class RagAnswerGrounding
    {
        /// <summary>
        /// Kaynak gösterimi için minimum ilgi skoru. 26.11.2026 gpt-oss-120b değerlendirmesinde doğru chunk'ların ilk sıra
        /// skorları 0,526–0,670; ilgisiz sonuçlar (squat tekniği sorusu) 0,365–0,384 aralığındaydı. Retrieval eşiği (0,35)
        /// değişmez; bu yalnızca bir passage'ın KAYNAK olarak gösterilebilmesi için ek eşiktir.
        /// </summary>
        public const double CitationMinScore = 0.45;

        /// <summary>Atıf için cevap ile passage metni arasında gereken asgari ortak içerik kökü sayısı.</summary>
        public const int MinSharedContentStems = 3;

        public static readonly Regex NumericExpression = new(
            @"(?<![\w])%?\d+(?:[.,]\d+)?(?:\s*[-–‑]\s*%?\d+(?:[.,]\d+)?)?\+?%?(?![\w])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly CultureInfo Tr = new("tr-TR");

        // ───────────────────────── Sentence splitting ─────────────────────────

        private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        {
            // Türkçe
            "ör", "örn", "vb", "vs", "bkz", "yak", "yakl", "dr", "prof", "doç", "sn", "dk", "sa", "no", "nr", "st", "vd", "ark", "bşk", "bk",
            // English
            "e.g", "i.e", "eg", "ie", "etc", "approx", "min", "sec", "cf", "fig", "mr", "mrs", "ms", "vol", "al", "vs", "no", "ca"
        };

        /// <summary>
        /// Metni cümlelere böler; her parça kendi sonundaki boşlukla döner (birleştirilince metin aynen oluşur).
        /// Kısaltmalardan, ondalık sayılardan, parantez içlerinden ve küçük harfle devam eden noktalardan sonra bölmez.
        /// </summary>
        public static List<string> SplitSentences(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text))
                return result;

            var start = 0;
            var depth = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c is '(' or '[' or '【') { depth++; continue; }
                if (c is ')' or ']' or '】') { if (depth > 0) depth--; continue; }
                if (c is not ('.' or '!' or '?' or '…')) continue;

                var end = i + 1;
                while (end < text.Length && text[end] is '.' or '!' or '?' or '…' or '"' or '\'' or '”' or '’' or '*' or ')')
                    end++;

                // Noktadan hemen sonra boşluk yoksa (2.5, e.g.x, URL) cümle sonu değildir.
                if (end < text.Length && !char.IsWhiteSpace(text[end])) { i = end - 1; continue; }
                if (depth > 0) { i = end - 1; continue; }
                if (c == '.' && IsAbbreviation(text, i)) { i = end - 1; continue; }

                var next = end;
                while (next < text.Length && text[next] is ' ' or '\t') next++;
                // Noktadan sonra küçük harfle devam ediyorsa (listede olmayan bir kısaltma) cümle sürer.
                if (c == '.' && next < text.Length && char.IsLower(text[next])) { i = end - 1; continue; }

                while (end < text.Length && text[end] is ' ' or '\t') end++;
                result.Add(text[start..end]);
                start = end;
                i = end - 1;
            }

            if (start < text.Length)
                result.Add(text[start..]);
            return result;
        }

        private static bool IsAbbreviation(string text, int dotIndex)
        {
            var j = dotIndex - 1;
            while (j >= 0 && (char.IsLetter(text[j]) || (text[j] == '.' && j > 0 && char.IsLetter(text[j - 1]))))
                j--;
            var word = text[(j + 1)..dotIndex];
            if (word.Length == 0)
                return false;
            if (word.Length == 1 && char.IsUpper(word[0]))
                return true; // baş harf: "J. Smith"
            return Abbreviations.Contains(word);
        }

        // ───────────────────────── Numeric grounding ─────────────────────────

        private static readonly Regex MarkdownPrefix = new(
            // Satır öneki: liste/başlık işareti ve (varsa) "**Etiket**:" kalın etiketi — içerik değiştirilse de korunur.
            @"^(?<p>\s*(?:#{1,6}\s+|[-*•+]\s+|\d{1,2}[.)]\s+|>\s+)?(?:\*\*[^*\r\n]{1,40}\*\*\s*:\s*)?)(?<c>.*)$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex Parenthetical = new(@"\s*\([^()]*\)", RegexOptions.Compiled);

        /// <summary>Desteklenmeyen sayısal iddiaları en küçük birimden (parantez → cümle) çıkarır. Passage yoksa metne dokunmaz.</summary>
        public static string ApplyNumericGrounding(string text, NumericGroundingEvidence evidence, out bool changed)
        {
            changed = false;
            if (string.IsNullOrWhiteSpace(text) || evidence.Passages.Count == 0)
                return text;

            var scheduleCaveat = HasScheduleCaveat(evidence.Passages);
            var output = new List<string>();
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) { output.Add(string.Empty); continue; }

                var m = MarkdownPrefix.Match(line);
                var prefix = m.Groups["p"].Value;
                var content = m.Groups["c"].Value;

                var kept = new StringBuilder();
                var lineChanged = false;
                foreach (var sentence in SplitSentences(content))
                {
                    var grounded = GroundSentence(sentence, evidence, scheduleCaveat);
                    if (!ReferenceEquals(grounded, sentence) && grounded != sentence)
                        lineChanged = true;
                    kept.Append(grounded);
                }

                if (!lineChanged) { output.Add(line); continue; }

                changed = true;
                var newContent = TidyInline(kept.ToString());
                if (!HasLetters(newContent))
                    continue; // satırın tamamı desteklenmeyen iddiaydı: liste işareti / yarım markdown bırakılmaz
                output.Add(prefix + newContent);
            }

            return changed ? CollapseBlankLines(string.Join("\n", output)).Trim() : text;
        }

        private static string GroundSentence(string sentence, NumericGroundingEvidence evidence, bool scheduleCaveat)
        {
            var matches = NumericExpression.Matches(sentence).Cast<Match>().ToList();
            if (matches.Count == 0)
                return sentence;

            // Kaynak sabit bir takvimi açıkça reddediyorsa, sayısal aralık sabit bir reçeteye dönüştürülemez.
            if (scheduleCaveat && IsPrescription(sentence, evidence.English))
                return string.Empty;

            if (matches.All(x => IsSupported(x, sentence, evidence)))
                return sentence;

            // En küçük birim: desteklenmeyen sayı yalnızca bir parantez içindeyse yalnızca o parantez çıkarılır.
            var withoutParens = Parenthetical.Replace(sentence, p =>
                NumericExpression.Matches(p.Value).Cast<Match>().Any(x => !IsSupported(x, p.Value, evidence, sentence)) ? string.Empty : p.Value);
            if (withoutParens != sentence &&
                NumericExpression.Matches(withoutParens).Cast<Match>().All(x => IsSupported(x, withoutParens, evidence)))
                return withoutParens;

            // Cümle düzeyinde desteklenmeyen nicel iddia: yalnızca bu cümle çıkarılır (sayıyı değiştirmek yanıltıcı olur).
            return string.Empty;
        }

        private static bool IsSupported(Match match, string sentence, NumericGroundingEvidence evidence, string? exerciseScope = null)
        {
            var value = match.Value;
            var normalized = RagGroundingContext.NormalizeNumber(value);

            if (evidence.TrustedNumbers.Contains(normalized) || evidence.IsPassageNumber(value))
                return true;

            var after = sentence[(match.Index + match.Length)..];

            // Uygulamanın kendi ACSM kuralı (%10) — kullanıcıya açıklanabilir.
            if (normalized is "%10" or "10%" || (normalized == "10" && Regex.IsMatch(after, @"^\s*%")))
                return true;

            // ACSM ≤%10 kuralıyla doğrulanmış kilo önerisi: aynı cümlede adı geçen egzersizin baseline'ına göre güvenli olmalı.
            if (Regex.IsMatch(after, @"^\s*(kg|kilo|kilogram)\b", RegexOptions.IgnoreCase))
            {
                var scope = exerciseScope ?? sentence;
                var index = exerciseScope is null ? match.Index : scope.IndexOf(value, StringComparison.Ordinal);
                var baseline = ResolveBaseline(scope, index, evidence.ExerciseBaselines);
                if (baseline is > 0)
                {
                    var parts = Regex.Matches(value, @"\d+(?:[.,]\d+)?").Cast<Match>()
                        .Select(p => double.Parse(p.Value.Replace(',', '.'), CultureInfo.InvariantCulture)).ToList();
                    return parts.Count > 0 && parts.All(kg => kg > 0 && AcsmProgressionRule.IsWithinLimit(kg, baseline.Value));
                }
            }

            return false;
        }

        // Cümledeki en yakın egzersiz adının baseline'ı (önce sayıdan önceki, yoksa sonraki). Adı geçmiyorsa null.
        private static double? ResolveBaseline(string sentence, int index, IReadOnlyDictionary<string, double> baselines)
        {
            if (index < 0)
                index = 0;
            (int Distance, bool Preceding, double Kg)? best = null;
            foreach (var (name, kg) in baselines)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var at = sentence.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                while (at >= 0)
                {
                    var preceding = at + name.Length <= index;
                    var distance = preceding ? index - (at + name.Length) : Math.Max(0, at - index);
                    if (best is null || (preceding && !best.Value.Preceding) ||
                        (preceding == best.Value.Preceding && distance < best.Value.Distance))
                        best = (distance, preceding, kg);
                    at = sentence.IndexOf(name, at + name.Length, StringComparison.OrdinalIgnoreCase);
                }
            }
            return best?.Kg;
        }

        private static bool HasScheduleCaveat(IReadOnlyList<RagGroundingPassage> passages) =>
            Regex.IsMatch(string.Join(" ", passages.Select(p => p.Text)), @"(?i)(sabit|kesin).{0,40}(aralık|takvim).{0,40}(öner|tavsiye)");

        private static bool IsPrescription(string sentence, bool english) =>
            Regex.IsMatch(sentence, english
                ? @"(?i)\b(should|must|recommend|recommended|do)\b"
                : @"(?i)\b(yapmalısın|uygula|öneriyorum|önerilir|yapılmalı)\b");

        // Boşluk/noktalama düzeltmesi ve yarım kalan kalın yazı işaretlerinin temizlenmesi.
        private static string TidyInline(string text)
        {
            var t = Regex.Replace(text, @"[ \t]{2,}", " ");
            t = Regex.Replace(t, @"\s+([,.;:!?])", "$1").Trim();
            t = Regex.Replace(t, @"^[,;:\-–]\s*", string.Empty);
            if (Regex.Matches(t, @"\*\*").Count % 2 == 1)
                t = t.Replace("**", string.Empty);
            return t.Trim();
        }

        private static bool HasLetters(string text) => text.Any(char.IsLetter);

        private static string CollapseBlankLines(string text) => Regex.Replace(text, @"\n{3,}", "\n\n");

        // ───────────────────────── Citations ─────────────────────────

        private static readonly Regex InternalMarkup = new(@"[ \t]*【[^】]*】", RegexOptions.Compiled);

        private static readonly Regex CitationLine = new(
            // "(Kaynak: …)" biçiminde açılan parantez varsa yalnızca o zaman kapanış parantezi tüketilir; "(2023)" korunur.
            @"(?im)(^|(?<=[.!?])[ \t]+)(?<open>\()?\s*(kaynak(lar)?|sources?|references?)\s*:[ \t]*(?<list>[^\r\n]*?)(?(open)\))[ \t]*\.?[ \t]*$",
            RegexOptions.Compiled);

        // "(Grgic et al., 2018; ACSM Position Stand, 2026)" gibi parantez içi akademik atıflar.
        private static readonly Regex ParenCitation = new(
            @"[ \t]*\((?<list>[^()]*?\b(19|20)\d{2}\b[^()]*?)\)",
            RegexOptions.Compiled);

        private static readonly Regex CitationCue = new(
            @"(et al|ve ark|ve arkadaş|\b[A-Z]{3,}\b|\b[A-ZÇĞİÖŞÜ][a-zçğıöşü]{2,}\b)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> GenericSourceWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "position", "stand", "guidelines", "guideline", "review", "systematic", "meta", "analysis", "study", "sports",
            "sport", "med", "and", "the", "for", "with", "into", "physical", "activity", "training", "resistance",
            "prescription", "consensus", "delphi", "kaynak", "kaynaklar", "source", "sources", "arkadaşları", "ark",
            "edition", "americans", "integrating", "strength", "muscular", "performance", "effect", "exerc", "open"
        };

        private static readonly HashSet<string> GenericStems = new(StringComparer.Ordinal)
        {
            "antre", "egzer", "squat", "bench", "press", "yapab", "olara", "gerek", "sağla", "şekil", "kulla", "edebi",
            "olabi", "yapma", "bunun", "birli", "sonra", "önemli", "kaynak", "göste", "yardı", "ayrıc", "durum"
        };

        /// <summary>Modelin dahili atıf işaretlemelerini (【…】) kaldırır.</summary>
        public static string StripInternalMarkup(string text) =>
            string.IsNullOrEmpty(text) ? text : Regex.Replace(InternalMarkup.Replace(text, string.Empty), @"[ \t]+([.,;:])", "$1");

        /// <summary>Retrieval yoksa: tüm kaynak satırlarını ve parantez içi akademik atıfları kaldırır.</summary>
        public static string RemoveAllCitations(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;
            var cleaned = CitationLine.Replace(text, string.Empty);
            cleaned = ParenCitation.Replace(cleaned, m => CitationCue.IsMatch(m.Groups["list"].Value) ? string.Empty : m.Value);
            cleaned = CollapseBlankLines(cleaned).Trim();
            return cleaned.Length == 0 ? text : cleaned;
        }

        /// <summary>
        /// Atıfları doğrular: her atıf parçası (";" ile ayrılmış) yalnızca ilgi skoru yeterli ve cevap içeriğiyle örtüşen bir
        /// passage'ın kaynak adında geçen ayırt edici bir ad (yazar/kurum) içeriyorsa kalır. Hiçbiri desteklenmiyorsa atıf düşer.
        /// </summary>
        public static string VerifyCitations(string text, IReadOnlyList<RagGroundingPassage> passages, double minScore = CitationMinScore)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var answerBody = ParenCitation.Replace(CitationLine.Replace(text, string.Empty), string.Empty);
            var answerStems = ContentStems(answerBody);
            var supporting = passages
                .Where(p => p.Score >= minScore)
                .Where(p => ContentStems(p.Title + " " + p.Text).Intersect(answerStems).Count() >= MinSharedContentStems)
                .ToList();

            string KeepSupported(string list) =>
                string.Join("; ", list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(part => supporting.Any(p => SharesSourceName(part, p.SourceName))));

            var cleaned = CitationLine.Replace(text, m =>
            {
                var kept = KeepSupported(m.Groups["list"].Value.TrimEnd('.', ' '));
                if (kept.Length == 0)
                    return string.Empty;
                return $"{m.Groups[1].Value}{(IsEnglishLabel(m.Value) ? "Source" : "Kaynak")}: {kept}";
            });

            cleaned = ParenCitation.Replace(cleaned, m =>
            {
                if (!CitationCue.IsMatch(m.Groups["list"].Value))
                    return m.Value; // akademik atıf değil (ör. "(2018 verisi)")
                var kept = KeepSupported(m.Groups["list"].Value);
                return kept.Length == 0 ? string.Empty : $" ({kept})";
            });

            cleaned = Regex.Replace(cleaned, @"[ \t]+([.,;:])", "$1");
            cleaned = CollapseBlankLines(cleaned).Trim();
            return cleaned.Length == 0 ? text : cleaned;
        }

        private static bool IsEnglishLabel(string citation) => Regex.IsMatch(citation, @"(?i)\b(sources?|references?)\s*:");

        private static bool SharesSourceName(string citationPart, string sourceName)
        {
            foreach (Match token in Regex.Matches(citationPart, @"\p{L}{3,}"))
            {
                if (GenericSourceWords.Contains(token.Value))
                    continue;
                if (Regex.IsMatch(sourceName, $@"(?i)(?<!\p{{L}}){Regex.Escape(token.Value)}(?!\p{{L}})"))
                    return true;
            }
            return false;
        }

        private static HashSet<string> ContentStems(string text) =>
            Regex.Split(text.ToLower(Tr), @"[^\p{L}]+")
                .Where(w => w.Length >= 5)
                .Select(w => w[..5])
                .Where(s => !GenericStems.Contains(s))
                .ToHashSet(StringComparer.Ordinal);
    }
}
