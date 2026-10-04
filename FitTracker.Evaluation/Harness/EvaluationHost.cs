using FitTracker.Evaluation.Instrumentation;
using FitTrackr.API.Data;
using FitTrackr.API.Plugins;
using FitTrackr.API.Repositories;
using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using FitTrackr.API.Validations;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace FitTracker.Evaluation.Harness;

/// <summary>
/// Tek bir değerlendirme case'i için izole bir production servis grafiği.
///
/// FitBot ile ilgili kayıtlar Program.cs'teki AYNI production extension metotları ve aynı lifetime'larla yapılır
/// (AddGroqSemanticKernel, AddFitBotWorkoutPlugin, AddFitBotWorkoutPlanPlugin, scoped AiWorkoutCoachService,
/// WorkoutRepository, WorkoutAnalysisService, AcsmGuardrailService, FluentValidation validator'ları). Tek fark:
/// SQL Server yerine her case'e özel SQLite in-memory fixture veritabanı.
///
/// Evaluation'a özgü eklentiler yalnızca ölçüm içindir ve davranışı değiştirmez:
///  - Groq HttpClient'ına eklenen <see cref="LlmCallRecorder"/> handler'ı (istek/yanıtı okur, aynen iletir),
///  - en içe kaydedilen <see cref="ToolInvocationRecorder"/> filtresi (next'i çağırır; sonucu/Terminate'i değiştirmez).
/// </summary>
public sealed class EvaluationHost : IAsyncDisposable
{
    /// <summary>Yeni NON-RAG baseline modeli. Açıkça sabitlenir; eski Llama sonuçlarıyla karıştırılmaz.</summary>
    public const string Model = "openai/gpt-oss-120b";

    private readonly SqliteConnection _connection;

    public ServiceProvider Services { get; }
    public LlmCallRecorder LlmCalls { get; }
    public ToolInvocationRecorder Tools { get; }

    private EvaluationHost(SqliteConnection connection, ServiceProvider services, LlmCallRecorder llmCalls, ToolInvocationRecorder tools)
    {
        _connection = connection;
        Services = services;
        LlmCalls = llmCalls;
        Tools = tools;
    }

    public static EvaluationHost Create(string groqApiKey, int maxLlmRequestsPerCase)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Groq:ApiKey"] = groqApiKey,
            ["Groq:Model"] = Model
        }).Build();

        var llmCalls = new LlmCallRecorder(maxLlmRequestsPerCase);
        var tools = new ToolInvocationRecorder();

        var services = new ServiceCollection();
        services.AddLogging(); // log sağlayıcısı yok: production log'ları konsolu/rapor dosyalarını kirletmez
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(configuration);

        // ── Production kayıtları (Program.cs ile aynı) ──
        services.AddDbContext<FitTrackrDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<IWorkoutRepository, WorkoutRepository>();
        services.AddScoped<IWorkoutAnalysisService, WorkoutAnalysisService>();
        services.AddSingleton<IAcsmGuardrailService, AcsmGuardrailService>();
        services.AddValidatorsFromAssemblyContaining<WorkoutRequestDtoValidator>();
        services.AddHttpClient();
        services.AddGroqSemanticKernel();
        services.AddFitBotWorkoutPlugin();
        services.AddFitBotWorkoutPlanPlugin();
        services.AddScoped<IAiWorkoutCoachService, AiWorkoutCoachService>();

        // ── Yalnızca ölçüm ──
        // Handler her pipeline için yeni örnek; kayıtlar paylaşılan recorder'a gider.
        services.AddHttpClient(GroqChatCompletion.HttpClientName)
            .AddHttpMessageHandler(() => new LlmRecordingHandler(llmCalls));
        // Production filtrelerinden SONRA kaydedilir → en içte çalışır, onların davranışını etkilemez.
        services.AddSingleton<IAutoFunctionInvocationFilter>(tools);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return new EvaluationHost(connection, provider, llmCalls, tools);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
