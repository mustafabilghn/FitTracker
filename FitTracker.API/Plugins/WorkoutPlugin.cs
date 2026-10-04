using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FitTrackr.API.Models.DTO;
using FitTrackr.API.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace FitTrackr.API.Plugins
{
    /// <summary>
    /// FitBot'un gerektiğinde çağırabildiği salt-okunur workout verisi fonksiyonları (Semantic Kernel function calling).
    ///
    /// Güvenlik: Hiçbir fonksiyon <c>userId</c> (veya başka bir kimlik/gizli bilgi) argümanı almaz. Veri her zaman
    /// <see cref="ICurrentUserContext"/> ile sunucu tarafında belirlenen kullanıcıya aittir; model başka bir
    /// kullanıcının verisini isteyemez. Model tool argümanlarına <c>userId</c> eklese bile argüman bağlanmaz.
    ///
    /// İş mantığı: Trend/plateau/son antrenman hesapları burada TEKRAR EDİLMEZ. Tek kaynak
    /// <see cref="IWorkoutAnalysisService.GetFitBotContextAsync"/>'tir; plugin yalnızca o çıktıyı dilimler. Böylece
    /// system prompt'a gömülen context ile tool sonuçları aynı hesaplamadan çıkar.
    ///
    /// Yaşam döngüsü: Scoped (EF Core DbContext'e dayanan servisleri kullanır). Bkz. <c>AddFitBotWorkoutPlugin</c>.
    /// </summary>
    public sealed class WorkoutPlugin
    {
        public const string PluginName = "Workout";

        private const int DefaultRecentWorkouts = 5;
        private const int MaxRecentWorkouts = 10; // context'in kendi üst sınırı

        private readonly IWorkoutAnalysisService _analysisService;
        private readonly ICurrentUserContext _currentUser;
        private readonly ILogger<WorkoutPlugin> _logger;

        public WorkoutPlugin(
            IWorkoutAnalysisService analysisService,
            ICurrentUserContext currentUser,
            ILogger<WorkoutPlugin> logger)
        {
            _analysisService = analysisService;
            _currentUser = currentUser;
            _logger = logger;
        }

        // Açıklamalar kısa tutulur: tool şeması her istekte input token olarak gider (Groq free plan TPM limiti).

        [KernelFunction, Description("Current user's most recent workouts (last 30 days, newest first) with exercises, set counts and max weight in kg.")]
        public Task<object> GetRecentWorkouts(
            [Description("How many workouts to return, 1-10. Default 5.")] int count = DefaultRecentWorkouts)
            => ReadAsync(nameof(GetRecentWorkouts), context =>
                context.RecentWorkouts.Take(Math.Clamp(count, 1, MaxRecentWorkouts)).ToList());

        [KernelFunction, Description("Current user's weekly max-weight trend (last 4 weeks, kg) per exercise, with UP/DOWN/STABLE trend.")]
        public Task<object> GetWeightTrends(
            [Description("Optional exercise name filter, e.g. 'Bench Press'. Omit for all exercises.")] string? exerciseName = null)
            => ReadAsync(nameof(GetWeightTrends), context =>
                string.IsNullOrWhiteSpace(exerciseName)
                    ? context.WeightTrends
                    : context.WeightTrends
                        .Where(t => t.ExerciseName.Contains(exerciseName.Trim(), StringComparison.OrdinalIgnoreCase))
                        .ToList());

        [KernelFunction, Description("Exercises where the current user's max weight has not changed for the last 3 weeks (plateau).")]
        public Task<object> GetPlateauExercises()
            => ReadAsync(nameof(GetPlateauExercises), context => context.PlateauExercises);

        private async Task<object> ReadAsync(string function, Func<FitBotContextDto, object> select)
        {
            var userId = _currentUser.UserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                // Fail closed: kullanıcı belirlenemediyse hiçbir veri okunmaz.
                _logger.LogWarning("FitBot tool {Function} called without a current user.", function);
                return new ToolError("The current user could not be identified.");
            }

            try
            {
                var context = await _analysisService.GetFitBotContextAsync(userId);
                return select(context);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Tool hatası uygulamayı çökertmez ve modele iç ayrıntı (SQL, stack trace) sızdırmaz:
                // detay loglanır, modele genel bir hata mesajı döner; model eldeki context ile yanıt verir.
                _logger.LogError(ex, "FitBot tool {Function} failed.", function);
                return new ToolError("Workout data is temporarily unavailable.");
            }
        }

        private sealed record ToolError([property: JsonPropertyName("error")] string Error);
    }
}
