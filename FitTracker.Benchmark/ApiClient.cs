using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FitTracker.Benchmark;

// Thin HTTP client for the two endpoints this tool needs: auth (to obtain a JWT) and the
// FitBot chat endpoint being benchmarked. No FitTracker.API project reference on purpose —
// this measures the real wire contract, the same way any other API consumer would see it.
public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> RegisterAndLoginAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var email = $"fitbot-bench-{suffix}@bench.fittracker.invalid";
        var username = $"fbbench{suffix}";
        var password = $"Bench{suffix}Aa1";

        var registerResp = await _http.PostAsJsonAsync("api/auth/register", new RegisterRequest
        {
            Username = username,
            Email = email,
            Password = password
        });

        if (!registerResp.IsSuccessStatusCode)
        {
            var body = await registerResp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Benchmark kullanıcısı oluşturulamadı ({(int)registerResp.StatusCode}): {Truncate(body, 300)}");
        }

        return await LoginAsync(email, password);
    }

    public async Task<string> LoginAsync(string email, string password)
    {
        var loginResp = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });

        if (!loginResp.IsSuccessStatusCode)
        {
            var body = await loginResp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Giriş başarısız ({(int)loginResp.StatusCode}): {Truncate(body, 300)}");
        }

        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        if (string.IsNullOrWhiteSpace(login?.JwtToken))
            throw new InvalidOperationException("Login yanıtında JWT token bulunamadı.");

        return login.JwtToken;
    }

    public async Task<RequestSample> SendChatAsync(int index, string token, string message, string actionType)
    {
        var sample = new RequestSample { Index = index, TimestampUtc = DateTime.UtcNow };

        var requestDto = new ChatRequest { Message = message, ActionType = actionType };
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/workout/fitbot/chat")
        {
            Content = JsonContent.Create(requestDto)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _http.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();
            stopwatch.Stop();

            sample.EndToEndMs = stopwatch.Elapsed.TotalMilliseconds;
            sample.HttpStatus = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                sample.Success = false;
                sample.Error = $"HTTP {(int)response.StatusCode}: {Truncate(responseBody, 300)}";
                return sample;
            }

            sample.Success = true;
            sample.ContextMs = TryReadDoubleHeader(response.Headers, "X-FitBot-Context-Ms");
            sample.GroqMs = TryReadDoubleHeader(response.Headers, "X-FitBot-Groq-Ms");
            sample.CacheStatus = TryReadStringHeader(response.Headers, "X-FitBot-Context-Cache");

            return sample;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            sample.Success = false;
            sample.EndToEndMs = stopwatch.Elapsed.TotalMilliseconds;
            sample.Error = ex.Message;
            return sample;
        }
    }

    private static double? TryReadDoubleHeader(HttpResponseHeaders headers, string name)
    {
        if (headers.TryGetValues(name, out var values) &&
            double.TryParse(values.FirstOrDefault(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? TryReadStringHeader(HttpResponseHeaders headers, string name)
    {
        return headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
