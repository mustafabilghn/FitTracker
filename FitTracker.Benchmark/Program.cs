using FitTracker.Benchmark;

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return 0;
}

var options = ParseArgs(args);
if (options is null)
    return 1;

Console.WriteLine("=".PadRight(60, '='));
Console.WriteLine("FitBot Chat Endpoint — Latency Benchmark");
Console.WriteLine("=".PadRight(60, '='));
Console.WriteLine($"Base URL           : {options.BaseUrl}");
Console.WriteLine($"Requests           : {options.Requests}");
Console.WriteLine($"Action type        : {options.ActionType}");
Console.WriteLine($"Fresh user/request : {options.FreshUserEveryRequest}");
Console.WriteLine($"Interval           : {options.IntervalMs} ms");
Console.WriteLine();

if (string.IsNullOrWhiteSpace(options.Token) && string.IsNullOrWhiteSpace(options.Email))
{
    Console.WriteLine(
        "UYARI: Ne --token ne de --email/--password verildi. Bu araç yeni bir throwaway " +
        "benchmark kullanıcısı oluşturacak (api/auth/register). Production'a karşı çalıştırıyorsanız " +
        "gerçek kullanıcı verisiyle karışmasın diye --token kullanmanız önerilir.");
    Console.WriteLine();
}

var handler = new HttpClientHandler();
if (options.Insecure)
{
    handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
}

using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };
var apiClient = new ApiClient(httpClient);

string sharedToken;
try
{
    sharedToken = await ResolveTokenAsync(apiClient, options);
}
catch (Exception ex)
{
    Console.WriteLine($"Kimlik doğrulama başarısız: {ex.Message}");
    return 1;
}

var samples = new List<RequestSample>();

for (var i = 1; i <= options.Requests; i++)
{
    var token = sharedToken;

    if (options.FreshUserEveryRequest && i > 1)
    {
        try
        {
            token = await apiClient.RegisterAndLoginAsync();
        }
        catch (Exception ex)
        {
            samples.Add(new RequestSample
            {
                Index = i,
                TimestampUtc = DateTime.UtcNow,
                Success = false,
                Error = $"Kullanıcı oluşturma/login hatası: {ex.Message}"
            });
            Console.WriteLine($"[{i,3}/{options.Requests}] KULLANICI HATASI: {ex.Message}");
            continue;
        }
    }

    var sample = await apiClient.SendChatAsync(i, token, options.Message, options.ActionType);
    samples.Add(sample);

    if (sample.Success)
    {
        var cache = sample.CacheStatus ?? "unknown";
        var ctx = sample.ContextMs?.ToString("F0") ?? "n/a";
        var groq = sample.GroqMs?.ToString("F0") ?? "n/a";
        Console.WriteLine(
            $"[{i,3}/{options.Requests}] OK   end-to-end={sample.EndToEndMs:F0}ms  context={ctx}ms ({cache})  groq={groq}ms");
    }
    else
    {
        Console.WriteLine($"[{i,3}/{options.Requests}] FAIL {sample.Error}");
    }

    if (i < options.Requests && options.IntervalMs > 0)
        await Task.Delay(options.IntervalMs);
}

var report = BuildReport(options, samples);

Directory.CreateDirectory(options.OutDir);
var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var jsonPath = Path.Combine(options.OutDir, $"fitbot-latency-{stamp}.json");
var mdPath = Path.Combine(options.OutDir, $"fitbot-latency-{stamp}.md");

Reporting.WriteJson(report, jsonPath);
Reporting.WriteMarkdown(report, mdPath);

Console.WriteLine();
Console.WriteLine("=".PadRight(60, '='));
Console.WriteLine($"Başarılı: {report.SuccessCount}/{report.RequestsAttempted}  |  Hata oranı: {report.ErrorRatePercent:F2}%");
if (report.SuccessCount < 20)
    Console.WriteLine("UYARI: Başarılı istek sayısı 20'nin altında. İstatistikler daha az güvenilir olabilir; --requests değerini artırıp tekrar çalıştırmayı düşünün.");
Console.WriteLine($"JSON rapor : {Path.GetFullPath(jsonPath)}");
Console.WriteLine($"Markdown   : {Path.GetFullPath(mdPath)}");
Console.WriteLine("=".PadRight(60, '='));

return 0;

static async Task<string> ResolveTokenAsync(ApiClient apiClient, BenchmarkOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.Token))
        return options.Token;

    if (!string.IsNullOrWhiteSpace(options.Email) && !string.IsNullOrWhiteSpace(options.Password))
        return await apiClient.LoginAsync(options.Email, options.Password);

    return await apiClient.RegisterAndLoginAsync();
}

static BenchmarkReport BuildReport(BenchmarkOptions options, List<RequestSample> samples)
{
    var successes = samples.Where(s => s.Success).ToList();
    var failures = samples.Where(s => !s.Success).ToList();

    var report = new BenchmarkReport
    {
        GeneratedAtUtc = DateTime.UtcNow,
        BaseUrl = options.BaseUrl,
        ActionType = options.ActionType,
        Message = options.Message,
        FreshUserEveryRequest = options.FreshUserEveryRequest,
        RequestsAttempted = samples.Count,
        SuccessCount = successes.Count,
        FailureCount = failures.Count,
        ErrorRatePercent = samples.Count == 0 ? 0 : failures.Count * 100.0 / samples.Count,
        Samples = samples,
        FailureMessages = failures.Select(f => $"#{f.Index}: {f.Error}").ToList()
    };

    report.EndToEndMs = Stats.Summarize(successes.Where(s => s.EndToEndMs.HasValue).Select(s => s.EndToEndMs!.Value));

    var withContext = successes.Where(s => s.ContextMs.HasValue).ToList();
    var withGroq = successes.Where(s => s.GroqMs.HasValue).ToList();

    report.ContextMs = Stats.Summarize(withContext.Select(s => s.ContextMs!.Value));
    report.GroqMs = Stats.Summarize(withGroq.Select(s => s.GroqMs!.Value));

    if (successes.Count > 0 && withContext.Count == 0)
    {
        report.Notes.Add(
            "Context retrieval / construction süresi ölçülemedi: hiçbir yanıt 'X-FitBot-Context-Ms' header'ını içermiyor. " +
            "Bu, benchmark'ın çalıştığı API instance'ının bu ölçüm enstrümantasyonunu (AiWorkoutCoachService.ChatAsync) " +
            "içermeyen daha eski bir build olduğu anlamına gelebilir. Uydurulmuş bir değer raporlanmadı.");
    }

    if (successes.Count > 0 && withGroq.Count == 0)
    {
        report.Notes.Add(
            "Groq API inference süresi ölçülemedi: hiçbir yanıt 'X-FitBot-Groq-Ms' header'ını içermiyor. " +
            "Aynı enstrümantasyon eksikliği geçerli. Uydurulmuş bir değer raporlanmadı.");
    }

    var hit = successes.Where(s => string.Equals(s.CacheStatus, "hit", StringComparison.OrdinalIgnoreCase)).ToList();
    var miss = successes.Where(s => string.Equals(s.CacheStatus, "miss", StringComparison.OrdinalIgnoreCase)).ToList();
    var unknown = successes.Where(s => s.CacheStatus is null).ToList();

    report.CacheHitCount = hit.Count;
    report.CacheMissCount = miss.Count;
    report.CacheUnknownCount = unknown.Count;

    report.EndToEndMsCacheHit = Stats.Summarize(hit.Where(s => s.EndToEndMs.HasValue).Select(s => s.EndToEndMs!.Value));
    report.EndToEndMsCacheMiss = Stats.Summarize(miss.Where(s => s.EndToEndMs.HasValue).Select(s => s.EndToEndMs!.Value));

    if (successes.Count > 0 && unknown.Count == successes.Count)
    {
        report.Notes.Add(
            "Cache hit/miss ayrımı yapılamadı: hiçbir yanıt 'X-FitBot-Context-Cache' header'ını içermiyor.");
    }
    else
    {
        if (hit.Count > 0 && hit.Count < 5)
            report.Notes.Add($"Cache HIT örneklem sayısı küçük (n={hit.Count}); bu bucket'taki istatistikler daha az güvenilir.");
        if (miss.Count > 0 && miss.Count < 5)
            report.Notes.Add($"Cache MISS örneklem sayısı küçük (n={miss.Count}); bu bucket'taki istatistikler daha az güvenilir.");
        if (miss.Count == 0)
            report.Notes.Add(
                "Hiç cache MISS örneği toplanamadı (varsayılan çalıştırmada context cache'in 1 dakikalık TTL'i içinde " +
                "aynı kullanıcı tekrar kullanıldığında bu beklenir). Cache MISS davranışını izole ölçmek için " +
                "--fresh-user-every-request bayrağıyla ayrı bir çalıştırma yapın.");
        if (hit.Count == 0 && !options.FreshUserEveryRequest)
            report.Notes.Add(
                "Hiç cache HIT örneği toplanamadı. İstekler arasındaki toplam süre (Groq gecikmesi dahil) context " +
                "cache TTL'ini (1 dakika) aşmış olabilir.");
    }

    return report;
}

static BenchmarkOptions? ParseArgs(string[] args)
{
    var options = new BenchmarkOptions();

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--base-url":
                options.BaseUrl = RequireValue(args, ref i, "--base-url");
                break;
            case "--requests":
                options.Requests = int.Parse(RequireValue(args, ref i, "--requests"));
                break;
            case "--message":
                options.Message = RequireValue(args, ref i, "--message");
                break;
            case "--action-type":
                options.ActionType = RequireValue(args, ref i, "--action-type");
                break;
            case "--token":
                options.Token = RequireValue(args, ref i, "--token");
                break;
            case "--email":
                options.Email = RequireValue(args, ref i, "--email");
                break;
            case "--password":
                options.Password = RequireValue(args, ref i, "--password");
                break;
            case "--fresh-user-every-request":
                options.FreshUserEveryRequest = true;
                break;
            case "--interval-ms":
                options.IntervalMs = int.Parse(RequireValue(args, ref i, "--interval-ms"));
                break;
            case "--insecure":
                options.Insecure = true;
                break;
            case "--out-dir":
                options.OutDir = RequireValue(args, ref i, "--out-dir");
                break;
            case "--help":
            case "-h":
                PrintUsage();
                return null;
            default:
                Console.WriteLine($"Bilinmeyen argüman: {args[i]}");
                PrintUsage();
                return null;
        }
    }

    if (options.Requests < 1)
    {
        Console.WriteLine("--requests en az 1 olmalı.");
        return null;
    }

    return options;
}

static string RequireValue(string[] args, ref int i, string flag)
{
    if (i + 1 >= args.Length)
        throw new ArgumentException($"{flag} bir değer bekliyor.");
    i++;
    return args[i];
}

static void PrintUsage()
{
    Console.WriteLine("""
        Kullanım: dotnet run --project FitTracker.Benchmark -- [seçenekler]

          --base-url <url>              API base URL (varsayılan: https://localhost:7100)
          --requests <n>                Deneme sayısı (varsayılan: 20)
          --message <text>              Sabit FitBot mesajı (karşılaştırılabilirlik için sabit tutulur)
          --action-type <type>          free|analyze|today|program|motivation (varsayılan: free)
          --token <jwt>                 Hazır JWT kullan (register/login atlanır) — production için önerilir
          --email <email> --password <pw>   Var olan hesapla login ol (register atlanır)
          --fresh-user-every-request     Her istek için yeni kullanıcı oluştur (cache MISS'i izole eder)
          --interval-ms <ms>             İstekler arası bekleme (varsayılan: 250)
          --insecure                     TLS sertifika doğrulamasını atla (yerel https dev sertifikaları için)
          --out-dir <path>               JSON/Markdown raporların yazılacağı klasör (varsayılan: results)
        """);
}
