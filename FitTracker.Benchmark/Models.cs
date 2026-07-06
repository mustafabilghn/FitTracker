namespace FitTracker.Benchmark;

// Local mirrors of the FitTracker.API request/response contracts. Kept intentionally separate
// from the API project: this tool exercises the real HTTP endpoint as an external client would,
// not the in-process C# types.
public class RegisterRequest
{
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LoginResponse
{
    public string? JwtToken { get; set; }
}

public class ChatRequest
{
    public string Message { get; set; } = "";
    public string ActionType { get; set; } = "free";
    public List<object> ConversationHistory { get; set; } = new();
}

// One measured attempt against POST /api/workout/fitbot/chat.
public class RequestSample
{
    public int Index { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool Success { get; set; }
    public int? HttpStatus { get; set; }
    public string? Error { get; set; }

    // Always measurable: client-side wall clock around the whole HTTP call.
    public double? EndToEndMs { get; set; }

    // Only measurable if the API response includes the corresponding timing header
    // (added as measurement-only instrumentation in AiWorkoutCoachService.ChatAsync).
    public double? ContextMs { get; set; }
    public double? GroqMs { get; set; }

    // "hit" | "miss" | null (header absent -> unknown, never guessed).
    public string? CacheStatus { get; set; }
}

public class StatSummary
{
    public int Count { get; set; }
    public double Mean { get; set; }
    public double Median { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double P95 { get; set; }
}

public class BenchmarkOptions
{
    public string BaseUrl { get; set; } = "https://localhost:7100";
    public int Requests { get; set; } = 20;
    public string Message { get; set; } = "Bugün için önerin nedir?";
    public string ActionType { get; set; } = "free";
    public string? Token { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public bool FreshUserEveryRequest { get; set; }
    public int IntervalMs { get; set; } = 250;
    public bool Insecure { get; set; }
    public string OutDir { get; set; } = "results";
}

public class BenchmarkReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string BaseUrl { get; set; } = "";
    public string ActionType { get; set; } = "";
    public string Message { get; set; } = "";
    public bool FreshUserEveryRequest { get; set; }

    public int RequestsAttempted { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double ErrorRatePercent { get; set; }

    public StatSummary? EndToEndMs { get; set; }
    public StatSummary? ContextMs { get; set; }
    public StatSummary? GroqMs { get; set; }

    public int CacheHitCount { get; set; }
    public int CacheMissCount { get; set; }
    public int CacheUnknownCount { get; set; }
    public StatSummary? EndToEndMsCacheHit { get; set; }
    public StatSummary? EndToEndMsCacheMiss { get; set; }

    // Explicit call-outs for anything that could not be measured, and why.
    public List<string> Notes { get; set; } = new();
    public List<string> FailureMessages { get; set; } = new();
    public List<RequestSample> Samples { get; set; } = new();
}
