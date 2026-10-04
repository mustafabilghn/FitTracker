using System.Text.RegularExpressions;

namespace FitTracker.Evaluation.Instrumentation;

/// <summary>Kayda giren her serbest metinden sırları ve hesap tanımlayıcılarını temizler.</summary>
public static partial class Redactor
{
    [GeneratedRegex(@"gsk_[A-Za-z0-9]+")] private static partial Regex GroqKey();
    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9._\-]+")] private static partial Regex Bearer();
    [GeneratedRegex(@"org_[A-Za-z0-9]+")] private static partial Regex GroqOrg();
    [GeneratedRegex(@"(?i)(password|pwd|secret|api[_-]?key)\s*[=:]\s*[^;,\s""]+")] private static partial Regex KeyValueSecret();

    public static string Clean(string? text, int maxLength = 400)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var cleaned = GroqKey().Replace(text, "gsk_<redacted>");
        cleaned = Bearer().Replace(cleaned, "Bearer <redacted>");
        cleaned = GroqOrg().Replace(cleaned, "org_<redacted>");
        cleaned = KeyValueSecret().Replace(cleaned, "$1=<redacted>");
        return cleaned.Length > maxLength ? cleaned[..maxLength] + "…" : cleaned;
    }
}
