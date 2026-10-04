using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FitTrackr.API.Data;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Plugins;
using FitTrackr.API.Repositories;
using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using FitTrackr.API.Validations;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Xunit;

namespace FitTracker.API.Tests;

/// <summary>
/// Hotfix: SaveWorkoutPlan intensity'yi yerelleştirilmiş Level metnine göre değil, stable seed ID'sine göre çözer.
///
/// EnsureCreated() yalnızca model seed'ini (İngilizce: Low/Medium/High) üretir; gerçek, migration uygulanmış veritabanında
/// ise UpdateSeeds migration'ı Level değerlerini Düşük/Orta/Yüksek yapmıştır. Bu testler production durumunu, gerçek
/// UpdateSeeds migration'ının KENDİ operasyonlarını SQLite üzerinde uygulayarak canlandırır.
/// </summary>
public class WorkoutPlanIntensityTests
{
    private const string UserA = "user-A";

    [Fact]
    public async Task Precondition_ProductionMigrationState_HasLocalizedLevels_AndNoEnglishMedium()
    {
        using var h = Harness.Create(productionSeedState: true);

        var levels = await h.IntensityLevelsAsync();

        Assert.Equal("Düşük", levels[IntensitySeedIds.Low]);
        Assert.Equal("Orta", levels[IntensitySeedIds.Medium]);
        Assert.Equal("Yüksek", levels[IntensitySeedIds.High]);
        Assert.DoesNotContain("Medium", levels.Values); // eski Level-tabanlı lookup burada null dönerdi
    }

    [Fact]
    public async Task SaveWorkoutPlan_OnProductionMigrationState_FindsOrta_AndPersistsThePlan()
    {
        using var h = Harness.Create(productionSeedState: true);

        var result = await h.SaveAsync(UserA, "Push", ("Bench Press", 60), ("Overhead Press", 40));

        Assert.True(result.Success, $"{result.Reason} {result.Detail}");
        var saved = await h.LoadAsync(result.WorkoutId!.Value);
        Assert.Equal(WorkoutStatus.Planned, saved.Status);
        Assert.Equal(UserA, saved.userId);
        Assert.Equal(2, saved.Exercises.Count);
        Assert.All(saved.Exercises, e =>
        {
            Assert.Equal(IntensitySeedIds.Medium, e.IntensityId);
            Assert.Equal("Orta", e.Intensity.Level);
        });
        Assert.Equal((1, 2, 2), await h.RowCountsAsync());
    }

    [Fact]
    public async Task SaveWorkoutPlan_ThroughChat_OnProductionMigrationState_ReturnsDeterministicSuccess()
    {
        using var h = Harness.Create(productionSeedState: true);
        h.Handler.EnqueueToolCall("c", "WorkoutPlan-SaveWorkoutPlan",
            """{"workoutName":"Push","exercises":[{"exerciseName":"Bench Press","sets":[{"reps":8,"weightInKg":60},{"reps":8,"weightInKg":60}]}]}""");

        var response = await h.ChatAsync(UserA, "Bana bir push planı oluştur ve kaydet.");

        Assert.Equal("\"Push\" antrenman planın başarıyla kaydedildi (1 egzersiz, 2 set).", response.Reply);
        Assert.Single(h.Handler.Requests); // hardening: write sonrası ikinci LLM çağrısı yok
        Assert.Equal((1, 1, 2), await h.RowCountsAsync());
    }

    [Fact]
    public async Task SaveWorkoutPlan_StillWorks_OnModelSeedState_EnglishLevels()
    {
        using var h = Harness.Create(productionSeedState: false);

        var result = await h.SaveAsync(UserA, "Push", ("Bench Press", 60));

        Assert.True(result.Success);
        var saved = await h.LoadAsync(result.WorkoutId!.Value);
        Assert.Equal("Medium", saved.Exercises.Single().Intensity.Level);
        Assert.Equal(IntensitySeedIds.Medium, saved.Exercises.Single().IntensityId);
    }

    [Fact]
    public async Task SaveWorkoutPlan_WhenTheReferenceRowIsMissing_FailsClosed_AndWritesNothing()
    {
        using var h = Harness.Create(productionSeedState: true);
        await h.DeleteIntensityAsync(IntensitySeedIds.Medium);

        var result = await h.SaveAsync(UserA, "Push", ("Bench Press", 60));

        Assert.False(result.Success);
        Assert.Equal("save_failed", result.Reason);
        Assert.Equal((0, 0, 0), await h.RowCountsAsync());
    }

    [Fact]
    public void SeedIds_AreTheSingleSourceForTheModelSeed()
    {
        using var h = Harness.Create(productionSeedState: false);
        using var scope = h.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();

        var designModel = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model; // seed verisi yalnızca design-time modelde tutulur
        var seededIds = designModel.FindEntityType(typeof(Intensity))!.GetSeedData().Select(row => (Guid)row["Id"]!).ToHashSet();

        Assert.Equal(new HashSet<Guid> { IntensitySeedIds.Low, IntensitySeedIds.Medium, IntensitySeedIds.High }, seededIds);
        Assert.Equal(IntensitySeedIds.Medium, WorkoutPlanPlugin.DefaultIntensityId);
    }

    // ───────────────────── Harness ─────────────────────

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();
        public List<string> Requests { get; } = new();

        public void EnqueueToolCall(string id, string name, string args) => _responses.Enqueue(JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            @object = "chat.completion",
            created = 1700000000,
            model = "openai/gpt-oss-120b",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[] { new { id, type = "function", function = new { name, arguments = args } } }
                    },
                    logprobs = (object?)null,
                    finish_reason = "tool_calls"
                }
            },
            usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 }
        }));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            if (_responses.Count == 0)
                throw new InvalidOperationException("Test beklenmeyen bir Groq çağrısı yaptı (kuyrukta cevap yok).");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ServiceProvider Provider { get; }
        public StubHandler Handler { get; } = new();

        private Harness(bool productionSeedState)
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = "test-key",
                ["Groq:Model"] = "openai/gpt-oss-120b"
            }).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddDbContext<FitTrackrDbContext>(o => o.UseSqlite(_connection));
            services.AddScoped<IWorkoutRepository, WorkoutRepository>();          // Program.cs ile aynı
            services.AddScoped<IWorkoutAnalysisService, WorkoutAnalysisService>();
            services.AddSingleton<IAcsmGuardrailService, AcsmGuardrailService>();
            services.AddValidatorsFromAssemblyContaining<WorkoutRequestDtoValidator>();
            services.AddGroqSemanticKernel();
            services.AddFitBotWorkoutPlugin();
            services.AddFitBotWorkoutPlanPlugin();
            services.AddScoped<IAiWorkoutCoachService, AiWorkoutCoachService>();
            services.AddHttpClient(GroqChatCompletion.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Handler);
            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            db.Database.EnsureCreated(); // model seed: Low/Medium/High

            if (productionSeedState)
            {
                // Production'daki gibi: gerçek UpdateSeeds migration'ının veri operasyonlarını uygula (Düşük/Orta/Yüksek, Ev, Spor Salonu).
                var operations = new FitTrackr.API.Migrations.UpdateSeeds().UpOperations.OfType<UpdateDataOperation>().ToList();
                Assert.NotEmpty(operations);
                foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model))
                    db.Database.ExecuteSqlRaw(command.CommandText);
            }
        }

        public static Harness Create(bool productionSeedState) => new(productionSeedState);

        public async Task<SaveWorkoutPlanResult> SaveAsync(string userId, string workoutName, params (string Name, double Kg)[] exercises)
        {
            using var scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(userId);
            var result = await scope.ServiceProvider.GetRequiredService<Kernel>().InvokeAsync(
                WorkoutPlanPlugin.PluginName, "SaveWorkoutPlan", new KernelArguments
                {
                    ["workoutName"] = workoutName,
                    ["exercises"] = exercises.Select(e => new WorkoutPlanExerciseDto
                    {
                        ExerciseName = e.Name,
                        Sets = new() { new WorkoutPlanSetDto { Reps = 8, WeightInKg = e.Kg } }
                    }).ToList()
                });
            return result.GetValue<SaveWorkoutPlanResult>()!;
        }

        public async Task<FitBotChatResponseDto> ChatAsync(string userId, string message)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IAiWorkoutCoachService>()
                .ChatAsync(userId, new FitBotChatRequestDto { Message = message, ActionType = "free" });
        }

        public async Task<Dictionary<Guid, string>> IntensityLevelsAsync()
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>().Intensities.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Level);
        }

        public async Task DeleteIntensityAsync(Guid id)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            db.Intensities.Remove(await db.Intensities.SingleAsync(i => i.Id == id));
            await db.SaveChangesAsync();
        }

        public async Task<Workout> LoadAsync(Guid id)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>().Workouts.AsNoTracking()
                .Include(w => w.Exercises).ThenInclude(e => e.Intensity)
                .SingleAsync(w => w.Id == id);
        }

        public async Task<(int Workouts, int Exercises, int Sets)> RowCountsAsync()
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            return (await db.Workouts.CountAsync(), await db.Exercises.CountAsync(), await db.ExerciseSets.CountAsync());
        }

        public void Dispose()
        {
            Provider.Dispose();
            _connection.Dispose();
        }
    }
}
