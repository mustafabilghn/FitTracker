using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FitTracker.Evaluation.Instrumentation;

public sealed record ToolCallRequest(string Id, string Name, string ArgumentsJson, IReadOnlyList<string> ArgumentKeys);

/// <summary>Modele giden tek bir HTTP isteği ve cevabı (yalnızca güvenli/özet alanlar).</summary>
public sealed class LlmCallRecord
{
    public int Index { get; init; }
    public int StatusCode { get; set; }
    public long LatencyMs { get; set; }

    // İstek (generation settings — production'ın gerçekte gönderdiği değerler)
    public string? RequestModel { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? MaxTokens { get; set; }
    public string? ResponseFormat { get; set; }
    public string? ToolChoice { get; set; }
    public bool? ParallelToolCalls { get; set; }
    public string? ReasoningEffort { get; set; }
    public IReadOnlyList<string> ToolsOffered { get; set; } = [];
    public IReadOnlyList<string> MessageRoles { get; set; } = [];
    public int RequestBytes { get; set; }

    // Cevap
    public string? ResponseModel { get; set; }
    public string? FinishReason { get; set; }
    public IReadOnlyList<ToolCallRequest> ToolCalls { get; set; } = [];
    public int ContentChars { get; set; }
    public int ReasoningChars { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }

    // Rate limit / hata
    public int? RateLimitRemainingTokens { get; set; }
    public string? RateLimitResetTokens { get; set; }
    public string? RetryAfter { get; set; }
    public string? Error { get; set; }
}

/// <summary>Bir case boyunca modele yapılan tüm HTTP çağrılarını toplar; güvenlik tavanını uygular.</summary>
public sealed class LlmCallRecorder
{
    private readonly int _maxRequests;
    private readonly List<LlmCallRecord> _calls = new();
    private readonly object _gate = new();

    public LlmCallRecorder(int maxRequests) => _maxRequests = maxRequests;

    public IReadOnlyList<LlmCallRecord> Calls
    {
        get { lock (_gate) return _calls.ToList(); }
    }

    internal LlmCallRecord Begin()
    {
        lock (_gate)
        {
            if (_calls.Count >= _maxRequests)
                throw new InvalidOperationException(
                    $"Evaluation safety cap: more than {_maxRequests} LLM requests in one case (unexpected tool loop). Aborted.");
            var record = new LlmCallRecord { Index = _calls.Count + 1 };
            _calls.Add(record);
            return record;
        }
    }
}

/// <summary>
/// Groq HttpClient pipeline'ına eklenen ölçüm handler'ı. İsteği ve cevabı OKUR (içerik tamponlanır, SDK aynen
/// tüketir), hiçbir şeyi değiştirmez, tekrar denemez. Authorization header'ı hiçbir zaman okunmaz/kaydedilmez.
/// </summary>
public sealed class LlmRecordingHandler : DelegatingHandler
{
    private static readonly Regex IdentityLikeKey = new("user|token|jwt|password|secret|conn|tenant|role", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly LlmCallRecorder _recorder;

    public LlmRecordingHandler(LlmCallRecorder recorder) => _recorder = recorder;

    public static bool IsIdentityLike(string key) => IdentityLikeKey.IsMatch(key);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var record = _recorder.Begin();
        var requestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        ParseRequest(requestBody, record);

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            record.LatencyMs = stopwatch.ElapsedMilliseconds;
            record.Error = Redactor.Clean($"{ex.GetType().Name}: {ex.Message}");
            throw;
        }

        var responseBody = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);
        record.LatencyMs = stopwatch.ElapsedMilliseconds;
        record.StatusCode = (int)response.StatusCode;
        record.RateLimitRemainingTokens = TryHeaderInt(response, "x-ratelimit-remaining-tokens");
        record.RateLimitResetTokens = TryHeader(response, "x-ratelimit-reset-tokens");
        record.RetryAfter = TryHeader(response, "retry-after");

        if (response.IsSuccessStatusCode)
            ParseResponse(responseBody, record);
        else
            record.Error = Redactor.Clean(responseBody);

        return response;
    }

    private static void ParseRequest(string body, LlmCallRecord record)
    {
        record.RequestBytes = body.Length;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            record.RequestModel = Str(root, "model");
            record.Temperature = Num(root, "temperature");
            record.TopP = Num(root, "top_p");
            record.MaxTokens = (int?)(Num(root, "max_completion_tokens") ?? Num(root, "max_tokens"));
            record.ReasoningEffort = Str(root, "reasoning_effort");
            if (root.TryGetProperty("response_format", out var rf))
                record.ResponseFormat = rf.ValueKind == JsonValueKind.Object && rf.TryGetProperty("type", out var t) ? t.GetString() : rf.ToString();
            if (root.TryGetProperty("tool_choice", out var tc))
                record.ToolChoice = tc.ValueKind == JsonValueKind.String ? tc.GetString() : tc.GetRawText();
            if (root.TryGetProperty("parallel_tool_calls", out var ptc) && ptc.ValueKind is JsonValueKind.True or JsonValueKind.False)
                record.ParallelToolCalls = ptc.GetBoolean();
            if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
                record.ToolsOffered = tools.EnumerateArray().Select(x => x.GetProperty("function").GetProperty("name").GetString() ?? "?").ToList();
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                record.MessageRoles = messages.EnumerateArray().Select(m => m.GetProperty("role").GetString() ?? "?").ToList();
        }
        catch (Exception ex)
        {
            record.Error = $"request parse failed: {ex.GetType().Name}";
        }
    }

    private static void ParseResponse(string body, LlmCallRecord record)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            record.ResponseModel = Str(root, "model");
            var choice = root.GetProperty("choices")[0];
            record.FinishReason = Str(choice, "finish_reason");
            var message = choice.GetProperty("message");
            record.ContentChars = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()!.Length : 0;
            record.ReasoningChars = message.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()!.Length : 0;

            if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
            {
                record.ToolCalls = calls.EnumerateArray().Select(call =>
                {
                    var function = call.GetProperty("function");
                    var args = function.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "{}" : "{}";
                    return new ToolCallRequest(
                        Str(call, "id") ?? "?",
                        Str(function, "name") ?? "?",
                        Redactor.Clean(args, 1500), // fixture verisi; yine de sınırlı ve temizlenmiş
                        ArgumentKeys(args));
                }).ToList();
            }

            if (root.TryGetProperty("usage", out var usage))
            {
                record.PromptTokens = (int?)Num(usage, "prompt_tokens");
                record.CompletionTokens = (int?)Num(usage, "completion_tokens");
                record.TotalTokens = (int?)Num(usage, "total_tokens");
            }
        }
        catch (Exception ex)
        {
            record.Error = $"response parse failed: {ex.GetType().Name}";
        }
    }

    private static IReadOnlyList<string> ArgumentKeys(string argumentsJson)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            Collect(doc.RootElement, keys);
        }
        catch
        {
            keys.Add("<unparseable>");
        }
        return keys.ToList();

        static void Collect(JsonElement e, SortedSet<string> keys)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject()) { keys.Add(p.Name); Collect(p.Value, keys); }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var i in e.EnumerateArray()) Collect(i, keys);
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string? TryHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    private static int? TryHeaderInt(HttpResponseMessage response, string name) =>
        int.TryParse(TryHeader(response, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
