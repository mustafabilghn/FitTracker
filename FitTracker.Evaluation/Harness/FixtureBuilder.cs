using System.Globalization;
using FitTrackr.API.Data;
using FitTrackr.API.Models.Domain;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Plugins;
using FitTrackr.API.Repositories;
using FitTrackr.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace FitTracker.Evaluation.Harness;

public static class EvalUsers
{
    public const string Primary = "eval-user-A";
    /// <summary>Sızıntı kontrolü: bu kullanıcının verisi Primary'nin hiçbir cevabında görünmemeli.</summary>
    public const string Other = "eval-user-B";

    /// <summary>Other kullanıcıya özgü işaretler (Primary'nin fixture'ında hiç yok).</summary>
    public static readonly string[] OtherUserMarkers = ["Hack Squat", "B Secret"];
}

/// <summary>
/// Deterministik fixture. Gerçek kullanıcı verisi KULLANILMAZ. Production kurallarıyla oluşturulur:
///  - Şema: EnsureCreated (model) + gerçek UpdateSeeds migration'ının veri operasyonları → production'daki gibi
///    Türkçe intensity seviyeleri (Düşük/Orta/Yüksek), stable seed ID'leri (IntensitySeedIds).
///  - Completed workout'lar: production IWorkoutRepository.CreateAsync (POST /api/Workout yolu → her zaman Completed).
///  - Planned workout'lar: production WorkoutPlanPlugin.SaveWorkoutPlan (doğrulama + guardrail + atomik kayıt → Planned).
///    Doğrudan çağrılır (LLM yok, ölçüm filtresi tetiklenmez).
///
/// Primary kullanıcı (bugüne göre gün):
///   Bench Press 90 (24) → 95 (17) → 97.5 (10) → 100 (3): haftalık trend UP, baseline 100 kg (sınır 110).
///   Squat 120 kg (16, 9, 2): üç ardışık hafta aynı → plateau, baseline 120 kg (sınır 132).
///   Overhead Press, Romanian Deadlift: ek trendler.
///   Planned: "Pull" (+2 gün), "Upper" (+5 gün, Bench Press 105 kg — baseline'a GİRMEMELİ).
/// Other kullanıcı: Hack Squat 250 kg, Bench Press 200 kg; Planned "B Secret Plan".
/// </summary>
public static class FixtureBuilder
{
    public static async Task SeedAsync(IServiceProvider services, DateTime todayUtc)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitTrackrDbContext>();
            await db.Database.EnsureCreatedAsync();

            // Production migration durumu: UpdateSeeds'in UpdateData operasyonları (Low/Medium/High → Düşük/Orta/Yüksek).
            var operations = new FitTrackr.API.Migrations.UpdateSeeds().UpOperations.OfType<UpdateDataOperation>().ToList();
            foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);

            var medium = await db.Intensities.AsNoTracking().SingleAsync(i => i.Id == IntensitySeedIds.Medium);
            if (medium.Level != "Orta")
                throw new InvalidOperationException($"Fixture is not in production seed state (Medium level = '{medium.Level}').");
        }

        // ── Primary: Completed geçmiş ──
        await CompletedAsync(services, EvalUsers.Primary, todayUtc, 24, "Push", ("Bench Press", 90, 8), ("Overhead Press", 45, 8));
        await CompletedAsync(services, EvalUsers.Primary, todayUtc, 17, "Push", ("Bench Press", 95, 8), ("Overhead Press", 47.5, 8));
        await CompletedAsync(services, EvalUsers.Primary, todayUtc, 16, "Legs", ("Squat", 120, 5), ("Romanian Deadlift", 90, 8));
        await CompletedAsync(services, EvalUsers.Primary, todayUtc, 10, "Push", ("Bench Press", 97.5, 8), ("Overhead Press", 47.5, 8));
        await CompletedAsync(services, EvalUsers.Primary, todayUtc, 9, "Legs", ("Squat", 120, 5), ("Romanian Deadlift", 95, 8));
        await CompletedAsync(services, EvalUsers.Primary, todayUtc, 3, "Push", ("Bench Press", 100, 8), ("Overhead Press", 50, 8));
        await CompletedAsync(services, EvalUsers.Primary, todayUtc, 2, "Legs", ("Squat", 120, 5), ("Romanian Deadlift", 100, 8));

        // ── Primary: Planned (production SaveWorkoutPlan) ──
        await PlannedAsync(services, EvalUsers.Primary, todayUtc.AddDays(2), "Pull", ("Barbell Row", 70), ("Lat Pulldown", 60));
        await PlannedAsync(services, EvalUsers.Primary, todayUtc.AddDays(5), "Upper", ("Bench Press", 105), ("Overhead Press", 52.5));

        // ── Other kullanıcı (sızıntı kontrolü) ──
        await CompletedAsync(services, EvalUsers.Other, todayUtc, 3, "Legs", ("Hack Squat", 250, 6), ("Bench Press", 200, 3));
        await PlannedAsync(services, EvalUsers.Other, todayUtc.AddDays(3), "B Secret Plan", ("Hack Squat", 255));
    }

    private static async Task CompletedAsync(
        IServiceProvider services, string userId, DateTime todayUtc, int daysAgo, string name,
        params (string Exercise, double Kg, int Reps)[] exercises)
    {
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWorkoutRepository>();

        var workout = new Workout
        {
            WorkoutName = name,
            WorkoutDate = todayUtc.Date.AddDays(-daysAgo),
            Exercises = exercises.Select(e => new Exercise
            {
                ExerciseName = e.Exercise,
                IntensityId = IntensitySeedIds.Medium,
                ExerciseSets = Enumerable.Range(1, 3).Select(n => new ExerciseSet
                {
                    SetNumber = n,
                    Reps = e.Reps.ToString(CultureInfo.InvariantCulture),
                    WeightInKg = e.Kg
                }).ToList()
            }).ToList()
        };

        var created = await repository.CreateAsync(workout, userId); // production: Status = Completed
        if (created.Status != WorkoutStatus.Completed)
            throw new InvalidOperationException("Fixture: production create path did not produce a Completed workout.");
    }

    private static async Task PlannedAsync(
        IServiceProvider services, string userId, DateTime date, string name, params (string Exercise, double Kg)[] exercises)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().SetUser(userId);
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        var result = (await kernel.InvokeAsync(WorkoutPlanPlugin.PluginName, WorkoutPlanPlugin.SaveFunctionName, new KernelArguments
        {
            ["workoutName"] = name,
            ["workoutDate"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["exercises"] = exercises.Select(e => new WorkoutPlanExerciseDto
            {
                ExerciseName = e.Exercise,
                Sets = Enumerable.Range(0, 3).Select(_ => new WorkoutPlanSetDto { Reps = 8, WeightInKg = e.Kg }).ToList()
            }).ToList()
        })).GetValue<SaveWorkoutPlanResult>();

        if (result is not { Success: true })
            throw new InvalidOperationException($"Fixture: SaveWorkoutPlan failed for '{name}': {result?.Reason} {result?.Detail}");
    }
}
