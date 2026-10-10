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

public sealed class EvaluationHost : IAsyncDisposable
{
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
        var repo = RepoInfo.Detect();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var configuration = BuildConfiguration(repo.Root, groqApiKey);
        var rag = configuration.GetSection("Rag").Get<FitTrackr.API.RAG.RagOptions>();
        if (rag is null || !rag.Enabled)
            throw new InvalidOperationException("RAG is disabled. Set Rag:Enabled=true in the API configuration.");

        var llmCalls = new LlmCallRecorder(maxLlmRequestsPerCase);
        var tools = new ToolInvocationRecorder();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<RagEvidenceRecorder>();

        services.AddDbContext<FitTrackrDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<IWorkoutRepository, WorkoutRepository>();
        services.AddScoped<IWorkoutAnalysisService, WorkoutAnalysisService>();
        services.AddSingleton<IAcsmGuardrailService, AcsmGuardrailService>();
        services.AddValidatorsFromAssemblyContaining<WorkoutRequestDtoValidator>();
        services.AddHttpClient();
        services.AddGroqSemanticKernel();
        services.AddFitBotWorkoutPlugin();
        services.AddFitBotWorkoutPlanPlugin();
        services.AddFitBotKnowledgePlugin(configuration);
        services.AddScoped<IAiWorkoutCoachService, AiWorkoutCoachService>();

        services.AddHttpClient(GroqChatCompletion.HttpClientName)
            .AddHttpMessageHandler(() => new LlmRecordingHandler(llmCalls));
        services.AddSingleton<IAutoFunctionInvocationFilter>(tools);
        services.AddSingleton<IAutoFunctionInvocationFilter>(sp => sp.GetRequiredService<RagEvidenceRecorder>());

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return new EvaluationHost(connection, provider, llmCalls, tools);
    }

    private static IConfiguration BuildConfiguration(string root, string groqApiKey)
    {
        return new ConfigurationBuilder()
            .SetBasePath(root)
            .AddJsonFile(Path.Combine("FitTracker.API", "appsettings.json"), optional: true)
            .AddUserSecrets("9e85e38b-49f7-43f6-b9fb-595494740b10")
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = groqApiKey,
                ["Groq:Model"] = Model,
                ["Rag:IngestOnStartup"] = "false"
            })
            .Build();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
