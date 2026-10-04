using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AutoMapper;
using FitTrackr.API.Controllers;
using FitTrackr.API.Data;
using FitTrackr.API.Mappings;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Plugins;
using FitTrackr.API.Repositories;
using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using FitTrackr.API.Validations;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
/// Phase 4: Planned vs Completed workout ayrımı. Gerçek WorkoutRepository, WorkoutAnalysisService, WorkoutController
/// (gerçek AutoMapper profili) ve production DI kayıtları + SQLite in-memory kullanılır. LLM transport'u sahte.
/// </summary>
public class PlannedWorkoutTests
{
    private const string UserA = "user-A";
    private const string UserB = "user-B";

    // ───────────────────── A. NEW NORMAL WORKOUT ─────────────────────

    [Fact]
    public async Task A_NormalWorkoutCreation_ThroughController_IsCompleted()
    {
        using var h = Harness.Create();

        var result = await h.Controller(UserA).Create(
            new WorkoutRequestDto { WorkoutName = "Push", WorkoutDate = DateTime.UtcNow.Date },
            new WorkoutRequestDtoValidator());

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<WorkoutSummaryDto>(created.Value);
        Assert.Equal(WorkoutStatus.Completed, dto.Status);
        Assert.Equal(WorkoutStatus.Completed, (await h.LoadAsync(dto.Id)).Status);
    }

    [Fact]
    public void A_ClientRequestDtos_CannotSetStatus()
    {
        // Normal create/update sözleşmelerinde Status yok: istemci Planned bir kayıt oluşturamaz/geri çeviremez.
        Assert.Null(typeof(WorkoutRequestDto).GetProperty("Status"));
        Assert.Null(typeof(UpdateWorkoutRequestDto).GetProperty("Status"));
    }

    [Fact]
    public void A_StatusIsSerializedAsString_LikeOtherApiEnums()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()); // Program.cs ile aynı
        var json = JsonSerializer.Serialize(new WorkoutSummaryDto { Status = WorkoutStatus.Planned }, options);
        Assert.Contains("\"Status\":\"Planned\"", json);
    }

    // ───────────────────── B. FITBOT PLAN → PLANNED ─────────────────────

    [Fact]
    public async Task B_SaveWorkoutPlan_PersistsAPlannedWorkout_WithHardenedLifecycleIntact()
    {
        using var h = Harness.Create();
        h.Handler.EnqueueToolCall("c", "WorkoutPlan-SaveWorkoutPlan",
            """{"workoutName":"Push","exercises":[{"exerciseName":"Bench Press","sets":[{"reps":8,"weightInKg":105}]}]}""");
        h.Handler.EnqueueReply("LLM-FINAL"); // ikinci completion yapılmamalı

        var response = await h.ChatAsync(UserA, "Bana bir push planı oluştur ve kaydet.");

        Assert.Equal("\"Push\" antrenman planın başarıyla kaydedildi (1 egzersiz, 1 set).", response.Reply);
        Assert.Single(h.Handler.Requests);
        var planned = Assert.Single(await h.PlannedAsync(UserA));
        Assert.Equal(WorkoutStatus.Planned, planned.Status);
        Assert.Equal(UserA, planned.userId);
    }

    // ───────────────────── C. EXISTING DATA MIGRATION ─────────────────────

    [Fact]
    public void C_Migration_OnlyAddsANonNullableStatusColumn_DefaultingToCompleted_WithoutDestructiveOperations()
    {
        Assert.Equal(0, (int)WorkoutStatus.Completed);

        var operations = new FitTrackr.API.Migrations.AddWorkoutStatus().UpOperations;

        var add = Assert.IsType<AddColumnOperation>(Assert.Single(operations));
        Assert.Equal("Workouts", add.Table);
        Assert.Equal("Status", add.Name);
        Assert.Equal(typeof(int), add.ClrType);
        Assert.False(add.IsNullable);
        Assert.Equal((int)WorkoutStatus.Completed, add.DefaultValue);
        Assert.DoesNotContain(operations, o => o is DropTableOperation or DropColumnOperation or DeleteDataOperation or AlterColumnOperation);
    }

    [Fact]
    public async Task C_Migration_AppliedToPreExistingRows_MarksThemAllCompleted_AndKeepsData()
    {
        // "Eski" şema (Status kolonu yok) + mevcut satırlar; ardından migration'ın KENDİ operasyonları SQLite SQL üreticisiyle uygulanır.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        Exec(connection, "CREATE TABLE Workouts (Id TEXT NOT NULL PRIMARY KEY, WorkoutName TEXT NOT NULL, WorkoutDate TEXT NOT NULL, userId TEXT NOT NULL);");
        Exec(connection, "INSERT INTO Workouts VALUES ('11111111-1111-1111-1111-111111111111','Push','2026-09-01 00:00:00','user-A'), " +
                         "('22222222-2222-2222-2222-222222222222','Legs','2026-09-02 00:00:00','user-B');");

        using var db = new FitTrackrDbContext(new DbContextOptionsBuilder<FitTrackrDbContext>().UseSqlite(connection).Options);
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(new FitTrackr.API.Migrations.AddWorkoutStatus().UpOperations, db.Model);
        foreach (var command in commands)
            Exec(connection, command.CommandText);

        var rows = Query(connection, "SELECT Id, WorkoutName, userId, Status FROM Workouts ORDER BY Id;");
        Assert.Equal(new[]
        {
            "11111111-1111-1111-1111-111111111111|Push|user-A|0",
            "22222222-2222-2222-2222-222222222222|Legs|user-B|0"
        }, rows);

        // Uygulama da bu satırları Completed olarak okur.
        Assert.All(await db.Workouts.AsNoTracking().ToListAsync(), w => Assert.Equal(WorkoutStatus.Completed, w.Status));
    }

    // ───────────────────── D / E. COMPLETED ANALYTICS, PLANNED EXCLUSION ─────────────────────

    [Fact]
    public async Task D_CompletedHistory_IsIncludedInAnalysisAndContext()
    {
        using var h = Harness.Create();

        var analysis = await h.AnalysisAsync(UserA);
        var context = await h.ContextAsync(UserA);

        Assert.Equal(2, analysis.TotalWorkouts);
        Assert.Equal(2, context.TotalWorkouts);
        Assert.Equal(3, context.DaysSinceLastWorkout);
        Assert.Equal(2, context.RecentWorkouts.Count);
        Assert.Contains(context.WeightTrends, t => t.ExerciseName == "Bench Press");
    }

    [Fact]
    public async Task E_PlannedWorkouts_AreExcludedFromAnalysisContextDashboardAndHistoryList()
    {
        using var h = Harness.Create();
        var before = (Analysis: await h.AnalysisAsync(UserA), Context: await h.ContextAsync(UserA), Dashboard: await h.DashboardAsync(UserA));

        // Bugün ve yarın için planlar (yapılmadı): hiçbir geçmiş metriğini değiştirmemeli.
        h.Seed(UserA, WorkoutStatus.Planned, daysAgo: 0, "Plan Today", ("Bench Press", 150));
        h.Seed(UserA, WorkoutStatus.Planned, daysAgo: -1, "Plan Tomorrow", ("Overhead Press", 70));

        var analysis = await h.AnalysisAsync(UserA);
        var context = await h.ContextAsync(UserA);
        var dashboard = await h.DashboardAsync(UserA);

        Assert.Equal(before.Analysis.TotalWorkouts, analysis.TotalWorkouts);
        Assert.Equal(before.Analysis.TotalExercises, analysis.TotalExercises);
        Assert.Equal(before.Analysis.TotalSets, analysis.TotalSets);
        Assert.Equal(before.Analysis.LastWorkoutDate, analysis.LastWorkoutDate);
        Assert.Equal(before.Analysis.WorkoutsLast30Days, analysis.WorkoutsLast30Days);

        Assert.Equal(before.Context.TotalWorkouts, context.TotalWorkouts);
        Assert.Equal(before.Context.DaysSinceLastWorkout, context.DaysSinceLastWorkout); // yarınki plan "son antrenman" değil
        Assert.Equal(before.Context.WorkoutsThisWeek, context.WorkoutsThisWeek);
        Assert.Equal(before.Context.WorkoutsLast30Days, context.WorkoutsLast30Days);
        Assert.Equal(before.Context.MuscleGroupFrequency, context.MuscleGroupFrequency);
        Assert.DoesNotContain(context.RecentWorkouts, w => w.WorkoutName.StartsWith("Plan"));
        Assert.DoesNotContain(context.RecentWorkouts.SelectMany(w => w.Exercises), e => e.MaxWeightKg == 150 || e.ExerciseName == "Overhead Press");

        Assert.Equal(100, dashboard.BenchPressMaxKg); // PR: planlanan 150 kg değil
        Assert.Equal(before.Dashboard.ActiveDates, dashboard.ActiveDates);

        var list = await h.HistoryListAsync(UserA); // GET /api/Workout: yapılmış antrenman listesi
        Assert.All(list, w => Assert.Equal(WorkoutStatus.Completed, w.Status));
        Assert.DoesNotContain(list, w => w.WorkoutName.StartsWith("Plan"));
    }

    // ───────────────────── F / G / H. TREND, PLATEAU, BASELINE EXCLUSION ─────────────────────

    [Fact]
    public async Task F_PlannedWeights_DoNotCreateOrChangeWeightTrends()
    {
        using var h = Harness.Create();
        // Deadlift yalnızca planlarda, iki farklı haftada → Completed olsaydı bir trend oluşurdu.
        h.Seed(UserA, WorkoutStatus.Planned, daysAgo: 0, "Plan 1", ("Deadlift", 180));
        h.Seed(UserA, WorkoutStatus.Planned, daysAgo: 8, "Plan 2", ("Deadlift", 170));
        // Bench için bu haftaya 150 kg plan → Completed olsaydı bu haftanın maksimumu 150 olurdu.
        h.Seed(UserA, WorkoutStatus.Planned, daysAgo: 1, "Plan 3", ("Bench Press", 150));

        var context = await h.ContextAsync(UserA);

        Assert.DoesNotContain(context.WeightTrends, t => t.ExerciseName == "Deadlift");
        var bench = Assert.Single(context.WeightTrends, t => t.ExerciseName == "Bench Press");
        Assert.Equal(100, bench.WeeklyMaxWeights.Single(w => w.WeeksAgo == 0).MaxKg);

        // Read tool da aynı (tek kaynak): plan ağırlıkları görünmez.
        var trends = await h.InvokeToolAsync<List<ExerciseWeightTrendDto>>(UserA, WorkoutPlugin.PluginName, "GetWeightTrends");
        Assert.DoesNotContain(trends, t => t.ExerciseName == "Deadlift");
        Assert.Equal(100, trends.Single(t => t.ExerciseName == "Bench Press").WeeklyMaxWeights.Max(w => w.MaxKg));
    }

    [Fact]
    public async Task G_PlannedWorkout_DoesNotCreateAPlateau()
    {
        using var h = Harness.Create();
        // Seed'de olmayan bir egzersiz: Completed Leg Press 200 kg (1 ve 2 hafta önce) → bu hafta veri yok, plateau değil.
        h.Seed(UserA, WorkoutStatus.Completed, daysAgo: 8, "Legs", ("Leg Press", 200));
        h.Seed(UserA, WorkoutStatus.Completed, daysAgo: 15, "Legs", ("Leg Press", 200));
        Assert.DoesNotContain("Leg Press", (await h.ContextAsync(UserA)).PlateauExercises);

        // Bu hafta için 200 kg Leg Press PLANI: Completed sayılsaydı 3 hafta aynı ağırlık → plateau olurdu.
        h.Seed(UserA, WorkoutStatus.Planned, daysAgo: 0, "Plan Legs", ("Leg Press", 200));

        Assert.DoesNotContain("Leg Press", (await h.ContextAsync(UserA)).PlateauExercises);
        Assert.DoesNotContain("Leg Press", await h.InvokeToolAsync<List<string>>(UserA, WorkoutPlugin.PluginName, "GetPlateauExercises"));

        // Kontrol: aynı ağırlık gerçekten YAPILIRSA plateau oluşur (senaryonun ayırt edici olduğunu kanıtlar).
        h.Seed(UserA, WorkoutStatus.Completed, daysAgo: 0, "Legs", ("Leg Press", 200));
        Assert.Contains("Leg Press", (await h.ContextAsync(UserA)).PlateauExercises);
    }

    [Fact]
    public async Task H_GuardrailBaseline_UsesOnlyCompletedHistory_NotPlannedWeights()
    {
        using var h = Harness.Create();
        // Completed Bench 100 kg + daha yeni tarihli Planned Bench 150 kg.
        h.Seed(UserA, WorkoutStatus.Planned, daysAgo: -1, "Big Plan", ("Bench Press", 150));
        h.Seed(UserA, WorkoutStatus.Planned, daysAgo: 0, "Big Plan 2", ("Bench Press", 150));

        // Baseline 150 olsaydı 115 kg güvenli (≤165) sayılırdı; doğru baseline 100 → sınır 110 → reddedilmeli.
        var rejected = await h.SaveAsync(UserA, "Push", ("Bench Press", 115));
        Assert.False(rejected.Success);
        Assert.Equal("guardrail_violation", rejected.Reason);
        Assert.Equal(110, rejected.LimitKg);

        var ok = await h.SaveAsync(UserA, "Push", ("Bench Press", 110));
        Assert.True(ok.Success);
    }

    // ───────────────────── I / J. PLANNED RETRIEVAL, USER ISOLATION ─────────────────────

    [Fact]
    public async Task I_CurrentUserSeesOnlyOwnPlannedWorkouts_ViaRepositoryControllerAndAgentTool()
    {
        using var h = Harness.Create();
        h.Seed(UserA, WorkoutStatus.Planned, daysAgo: -2, "A Pull", ("Barbell Row", 60));
        h.Seed(UserA, WorkoutStatus.Planned, daysAgo: -1, "A Push", ("Bench Press", 105));
        h.Seed(UserB, WorkoutStatus.Planned, daysAgo: -1, "B Secret", ("Bench Press", 210));

        // Repository: yalnızca A'nın planları, en yakın tarih önce; Completed geçmiş dahil değil.
        var planned = await h.PlannedAsync(UserA);
        Assert.Equal(new[] { "A Push", "A Pull" }, planned.Select(w => w.WorkoutName).ToArray());
        Assert.All(planned, w => Assert.Equal(WorkoutStatus.Planned, w.Status));

        // Controller: GET /api/Workout/planned
        var ok = Assert.IsType<OkObjectResult>(await h.Controller(UserA).GetPlanned());
        var dtos = Assert.IsType<List<WorkoutSummaryDto>>(ok.Value);
        Assert.Equal(new[] { "A Push", "A Pull" }, dtos.Select(d => d.WorkoutName).ToArray());
        Assert.All(dtos, d => Assert.Equal(WorkoutStatus.Planned, d.Status));

        // Agent tool (userId parametresi yok; current user server-side).
        var function = await h.KernelFunctionAsync(WorkoutPlanPlugin.PluginName, "GetPlannedWorkouts");
        Assert.Empty(function.Metadata.Parameters);
        var plans = await h.InvokeToolAsync<List<PlannedWorkoutSummary>>(UserA, WorkoutPlanPlugin.PluginName, "GetPlannedWorkouts");
        Assert.Equal(new[] { "A Push", "A Pull" }, plans.Select(p => p.WorkoutName).ToArray());
        Assert.Equal(105, plans[0].Exercises.Single().MaxKg);

        // Kullanıcı yoksa: fail closed.
        var anonymous = await h.InvokeToolAsync<object>(userId: null, WorkoutPlanPlugin.PluginName, "GetPlannedWorkouts");
        Assert.Contains("could not be identified", JsonSerializer.Serialize(anonymous));
    }

    [Fact]
    public async Task I_AgentCanAnswerWhatAreMyPlans_ThroughReadFlow()
    {
        using var h = Harness.Create();
        h.Seed(UserA, WorkoutStatus.Planned, daysAgo: -1, "A Push", ("Bench Press", 105));
        h.Seed(UserB, WorkoutStatus.Planned, daysAgo: -1, "B Secret", ("Bench Press", 210));
        h.Handler.EnqueueToolCall("r", "WorkoutPlan-GetPlannedWorkouts", "{}");
        h.Handler.EnqueueReply("Yarın için Push planın var.");

        var response = await h.ChatAsync(UserA, "Benim kayıtlı planlarım neler?");

        Assert.Equal("Yarın için Push planın var.", response.Reply);
        Assert.Equal(2, h.Handler.Requests.Count); // okuma: LLM → tool → LLM (write lifecycle'ı değil)
        var toolContent = LastToolContent(h.Handler.Requests[1]);
        Assert.Contains("A Push", toolContent);
        Assert.DoesNotContain("B Secret", toolContent);
        Assert.DoesNotContain("210", toolContent);
    }

    [Fact]
    public async Task J_UserA_CannotCompleteOrSeeUserBsPlannedWorkout()
    {
        using var h = Harness.Create();
        var bPlan = h.Seed(UserB, WorkoutStatus.Planned, daysAgo: -1, "B Plan", ("Squat", 200));

        var result = await h.Controller(UserA).Complete(bPlan);

        Assert.IsType<NotFoundResult>(result); // varlığı sızdırılmaz
        Assert.Equal(WorkoutStatus.Planned, (await h.LoadAsync(bPlan)).Status);
        Assert.Empty(await h.PlannedAsync(UserA));
        Assert.Equal(1, (await h.AnalysisAsync(UserB)).TotalWorkouts); // B'nin planı Completed olmadı (yalnızca seed'deki 1)

        Assert.IsType<NotFoundResult>(await h.Controller(UserA).Complete(Guid.NewGuid()));
    }

    // ───────────────────── K / L. TRANSITIONS ─────────────────────

    [Fact]
    public async Task K_CompletingAPlan_MakesItCompleted_AndItThenCountsInAnalysis()
    {
        using var h = Harness.Create();
        var plan = h.Seed(UserA, WorkoutStatus.Planned, daysAgo: -2, "Push Plan", ("Bench Press", 105)); // 2 gün sonrası için
        var before = await h.ContextAsync(UserA);

        var ok = Assert.IsType<OkObjectResult>(await h.Controller(UserA).Complete(plan));

        var dto = Assert.IsType<WorkoutSummaryDto>(ok.Value);
        Assert.Equal(WorkoutStatus.Completed, dto.Status);
        var saved = await h.LoadAsync(plan);
        Assert.Equal(WorkoutStatus.Completed, saved.Status);
        Assert.Equal(DateTime.UtcNow.Date, saved.WorkoutDate.Date); // ileri tarihli plan bugün yapıldı

        var after = await h.ContextAsync(UserA);
        Assert.Equal(before.TotalWorkouts + 1, after.TotalWorkouts);
        Assert.Equal(0, after.DaysSinceLastWorkout);
        Assert.Contains(after.RecentWorkouts, w => w.WorkoutName == "Push Plan");
        Assert.Equal(105, after.WeightTrends.Single(t => t.ExerciseName == "Bench Press").WeeklyMaxWeights.Single(w => w.WeeksAgo == 0).MaxKg);
        Assert.Empty(await h.PlannedAsync(UserA));
    }

    [Fact]
    public async Task K_CompletingAPastDatedPlan_KeepsItsDate()
    {
        using var h = Harness.Create();
        var plan = h.Seed(UserA, WorkoutStatus.Planned, daysAgo: 1, "Yesterday Plan", ("Barbell Row", 60));

        Assert.IsType<OkObjectResult>(await h.Controller(UserA).Complete(plan));

        Assert.Equal(DateTime.UtcNow.Date.AddDays(-1), (await h.LoadAsync(plan)).WorkoutDate.Date);
    }

    [Fact]
    public async Task L_CompletedWorkout_CannotGoBackToPlanned_CompleteIsIdempotent_UpdateIgnoresStatus()
    {
        using var h = Harness.Create();
        var done = h.Seed(UserA, WorkoutStatus.Completed, daysAgo: 1, "Done", ("Squat", 100));
        var dateBefore = (await h.LoadAsync(done)).WorkoutDate;

        // Tekrar "complete": idempotent, hiçbir şey değişmez.
        var ok = Assert.IsType<OkObjectResult>(await h.Controller(UserA).Complete(done));
        Assert.Equal(WorkoutStatus.Completed, Assert.IsType<WorkoutSummaryDto>(ok.Value).Status);
        Assert.Equal(dateBefore, (await h.LoadAsync(done)).WorkoutDate);

        // Repository update'i Status taşısa bile durumu değiştirmez (Completed → Planned geçişi yok).
        using (var scope = h.Provider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IWorkoutRepository>();
            await repo.UpdateAsync(done, new Workout { WorkoutName = "Renamed", WorkoutDate = dateBefore, Status = WorkoutStatus.Planned });
        }

        var after = await h.LoadAsync(done);
        Assert.Equal("Renamed", after.WorkoutName);
        Assert.Equal(WorkoutStatus.Completed, after.Status);
        Assert.Empty(await h.PlannedAsync(UserA));
    }

    // ───────────────────── Harness ─────────────────────

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static List<string> Query(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture))));
        return rows;
    }

    private static string LastToolContent(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        return doc.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();
        public List<string> Requests { get; } = new();

        public void EnqueueReply(string content) => _responses.Enqueue(Completion(new { role = "assistant", content }, "stop"));

        public void EnqueueToolCall(string id, string name, string args) => _responses.Enqueue(Completion(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = new[] { new { id, type = "function", function = new { name, arguments = args } } }
        }, "tool_calls"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            if (_responses.Count == 0)
                throw new InvalidOperationException("Test beklenmeyen bir Groq çağrısı yaptı (kuyrukta cevap yok).");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json") };
        }

        private static string Completion(object message, string finishReason) => JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            @object = "chat.completion",
            created = 1700000000,
            model = "openai/gpt-oss-120b",
            choices = new[] { new { index = 0, message, logprobs = (object?)null, finish_reason = finishReason } },
            usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 }
        });
    }

    private sealed class Harness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly Guid _medium;

        public ServiceProvider Provider { get; }
        public StubHandler Handler { get; } = new();

        private Harness()
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
            services.AddAutoMapper(typeof(AutoMapperProfiles));
            services.AddGroqSemanticKernel();
            services.AddFitBotWorkoutPlugin();
            services.AddFitBotWorkoutPlanPlugin();
            services.AddScoped<IAiWorkoutCoachService, AiWorkoutCoachService>();
            services.AddHttpClient(GroqChatCompletion.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Handler);
            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            db.Database.EnsureCreated();
            _medium = db.Intensities.Single(i => i.Level == "Medium").Id;

            // Completed geçmiş: A → Bench 95 (10 gün önce), Bench 100 + Squat 120 (3 gün önce); B → Bench 200.
            Seed(UserA, WorkoutStatus.Completed, 10, "Push", ("Bench Press", 95));
            Seed(UserA, WorkoutStatus.Completed, 3, "Push", ("Bench Press", 100), ("Squat", 120));
            Seed(UserB, WorkoutStatus.Completed, 3, "Push", ("Bench Press", 200));
        }

        public static Harness Create() => new();

        /// <summary>daysAgo negatifse gelecek tarih.</summary>
        public Guid Seed(string userId, WorkoutStatus status, int daysAgo, string name, params (string Name, double Kg)[] exercises)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            var workout = new Workout
            {
                WorkoutName = name,
                WorkoutDate = DateTime.UtcNow.Date.AddDays(-daysAgo),
                userId = userId,
                Status = status,
                Exercises = exercises.Select(e => new Exercise
                {
                    ExerciseName = e.Name,
                    IntensityId = _medium,
                    ExerciseSets = new() { new ExerciseSet { SetNumber = 1, Reps = "5", WeightInKg = e.Kg } }
                }).ToList()
            };
            db.Workouts.Add(workout);
            db.SaveChanges();
            return workout.Id;
        }

        public WorkoutController Controller(string userId)
        {
            var scope = Provider.CreateScope(); // controller ömrü = test (scope'u dispose etmek gerekmiyor; provider dispose eder)
            return new WorkoutController(
                scope.ServiceProvider.GetRequiredService<IMapper>(),
                scope.ServiceProvider.GetRequiredService<IWorkoutRepository>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "test"))
                    }
                }
            };
        }

        public async Task<FitBotChatResponseDto> ChatAsync(string userId, string message)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IAiWorkoutCoachService>()
                .ChatAsync(userId, new FitBotChatRequestDto { Message = message, ActionType = "free" });
        }

        public async Task<SaveWorkoutPlanResult> SaveAsync(string userId, string workoutName, params (string Name, double Kg)[] exercises)
        {
            using var scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(userId);
            var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();
            var result = await kernel.InvokeAsync(WorkoutPlanPlugin.PluginName, "SaveWorkoutPlan", new KernelArguments
            {
                ["workoutName"] = workoutName,
                ["exercises"] = exercises.Select(e => new WorkoutPlanExerciseDto
                {
                    ExerciseName = e.Name,
                    Sets = new() { new WorkoutPlanSetDto { Reps = 5, WeightInKg = e.Kg } }
                }).ToList()
            });
            return result.GetValue<SaveWorkoutPlanResult>()!;
        }

        public async Task<KernelFunction> KernelFunctionAsync(string plugin, string function)
        {
            using var scope = Provider.CreateScope();
            await Task.CompletedTask;
            return scope.ServiceProvider.GetRequiredService<Kernel>().Plugins[plugin][function];
        }

        public async Task<T> InvokeToolAsync<T>(string? userId, string plugin, string function)
        {
            using var scope = Provider.CreateScope();
            if (userId is not null)
                scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(userId);
            var result = await scope.ServiceProvider.GetRequiredService<Kernel>().InvokeAsync(plugin, function);
            return result.GetValue<T>()!;
        }

        public async Task<WorkoutAnalysisDto> AnalysisAsync(string userId)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IWorkoutAnalysisService>().GetAnalysisAsync(userId);
        }

        public async Task<FitBotContextDto> ContextAsync(string userId)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IWorkoutAnalysisService>().GetFitBotContextAsync(userId);
        }

        public async Task<DashboardSummaryDto> DashboardAsync(string userId)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IWorkoutRepository>().GetDashboardAsync(userId);
        }

        public async Task<List<Workout>> HistoryListAsync(string userId)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IWorkoutRepository>().GetAllAsync(userId);
        }

        public async Task<List<Workout>> PlannedAsync(string userId)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IWorkoutRepository>().GetPlannedAsync(userId);
        }

        public async Task<Workout> LoadAsync(Guid id)
        {
            using var scope = Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>().Workouts.AsNoTracking().SingleAsync(w => w.Id == id);
        }

        public void Dispose()
        {
            Provider.Dispose();
            _connection.Dispose();
        }
    }
}
