using System.Globalization;
using System.Text.RegularExpressions;

namespace FitTracker.Evaluation.Instrumentation;

/// <summary>
/// YALNIZCA değerlendirme aracı içindir (production Groq istemcisi retry yapmaz ve değişmez).
/// Groq rate-limit başlıklarını izler:
///  - x-ratelimit-remaining-tokens / x-ratelimit-reset-tokens → dakikalık token (TPM) penceresi,
///  - x-ratelimit-remaining-requests / x-ratelimit-reset-requests → günlük istek (RPD) penceresi,
///  - retry-after → 429 sonrası beklenmesi gereken süre.
/// İstekler sıralıdır (paralel istek yok). TPM/RPM kaynaklı 429'da Retry-After kadar beklenip aynı istek en fazla
/// <see cref="MaxRetriesPerRequest"/> kez yeniden gönderilir. Günlük limit (TPD/RPD) tükenirse <see cref="DailyLimitReached"/>
/// set edilir; runner'lar koşuyu durdurur ve sonucu "eksik" olarak işaretler.
/// </summary>
public static class GroqRateLimitGate
{
    public const int MaxRetriesPerRequest = 2;
    private static readonly TimeSpan MaxSingleWait = TimeSpan.FromSeconds(90);
    private static readonly object Gate = new();

    private static int? _remainingTokens;
    private static DateTimeOffset? _tokensResetAt;

    /// <summary>Bir isteğin tahmini token ihtiyacı; kalan TPM bunun altındaysa pencerenin sıfırlanması beklenir.</summary>
    public static int MinRemainingTokensBeforeRequest { get; set; } = 5000;

    public static bool DailyLimitReached { get; private set; }
    public static string? DailyLimitDetail { get; private set; }

    /// <summary>İstekten önce: günlük limit doluysa durur, TPM penceresi düşükse sıfırlanmasını bekler. Bekleme süresini döndürür.</summary>
    public static async Task<TimeSpan> WaitBeforeRequestAsync(CancellationToken cancellationToken)
    {
        if (DailyLimitReached)
            throw new GroqDailyLimitException(DailyLimitDetail ?? "daily limit reached");

        TimeSpan wait;
        lock (Gate)
        {
            wait = _remainingTokens is { } remaining && remaining < MinRemainingTokensBeforeRequest && _tokensResetAt is { } reset
                ? Clamp(reset - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(500))
                : TimeSpan.Zero;
        }

        if (wait > TimeSpan.Zero)
        {
            Console.WriteLine($"    (rate-limit: remaining TPM low, waiting {wait.TotalSeconds:F1}s before request)");
            await Task.Delay(wait, cancellationToken);
            lock (Gate) { _remainingTokens = null; _tokensResetAt = null; }
        }
        return wait;
    }

    public static void Observe(HttpResponseMessage response)
    {
        var remainingTokens = HeaderInt(response, "x-ratelimit-remaining-tokens");
        var resetTokens = ParseDuration(Header(response, "x-ratelimit-reset-tokens"));
        var remainingRequests = HeaderInt(response, "x-ratelimit-remaining-requests");

        lock (Gate)
        {
            if (remainingTokens is not null)
            {
                _remainingTokens = remainingTokens;
                _tokensResetAt = resetTokens is { } r ? DateTimeOffset.UtcNow + r : DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
            }
        }

        // Groq'ta "requests" penceresi günlük istek (RPD) limitidir.
        if (remainingRequests is 0)
            MarkDailyLimit("x-ratelimit-remaining-requests=0 (daily request limit)");
    }

    /// <summary>429 gövdesi günlük limiti (TPD/RPD) mi gösteriyor? Öyleyse beklemek anlamsızdır; koşu durdurulur.</summary>
    public static bool IsDailyLimit(string? responseBody) =>
        responseBody is not null &&
        Regex.IsMatch(responseBody, @"per day|\(TPD\)|\(RPD\)|tokens per day|requests per day", RegexOptions.IgnoreCase);

    public static void MarkDailyLimit(string detail)
    {
        DailyLimitReached = true;
        DailyLimitDetail = detail;
    }

    /// <summary>429 sonrası bekleme: önce Retry-After, yoksa x-ratelimit-reset-tokens, yoksa 60 sn (en fazla 90 sn).</summary>
    public static TimeSpan RetryDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return Clamp(delta + TimeSpan.FromMilliseconds(500));
        if (response.Headers.RetryAfter?.Date is { } date)
            return Clamp(date - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(500));
        if (ParseDuration(Header(response, "retry-after")) is { } parsed)
            return Clamp(parsed + TimeSpan.FromMilliseconds(500));
        if (ParseDuration(Header(response, "x-ratelimit-reset-tokens")) is { } reset)
            return Clamp(reset + TimeSpan.FromMilliseconds(500));
        return TimeSpan.FromSeconds(60);
    }

    private static TimeSpan Clamp(TimeSpan value) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value > MaxSingleWait ? MaxSingleWait : value;

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    private static int? HeaderInt(HttpResponseMessage response, string name) =>
        int.TryParse(Header(response, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>"7.66s", "1m2.5s", "250ms" veya düz saniye ("12").</summary>
    public static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var plainSeconds))
            return TimeSpan.FromSeconds(plainSeconds);

        var total = 0.0;
        var matched = false;
        foreach (Match m in Regex.Matches(value, @"(?<n>\d+(?:\.\d+)?)(?<u>ms|h|m|s)"))
        {
            matched = true;
            var n = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            total += m.Groups["u"].Value switch { "ms" => n / 1000, "h" => n * 3600, "m" => n * 60, _ => n };
        }
        return matched ? TimeSpan.FromSeconds(total) : null;
    }
}

public sealed class GroqDailyLimitException(string detail) : Exception($"Groq daily limit reached: {detail}");
