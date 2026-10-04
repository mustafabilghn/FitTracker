using System;
using System.Collections.Generic;
using System.Linq;
using FitTrackr.API.Models.DTO;

namespace FitTrackr.API.Services
{
    /// <summary>
    /// ACSM progressive overload kuralının TEK kaynağı: önerilen ağırlık, egzersizin baseline'ının en fazla %10 üstü olabilir
    /// (Δweight / weight_prev ≤ 0.10). Tam %10 güvenlidir.
    ///
    /// İki kullanım alanı aynı kuralı paylaşır:
    ///  1) <see cref="AcsmGuardrailService"/> — chat cevabındaki metin önerilerini sınıra çeker (mevcut davranış).
    ///  2) Yapılandırılmış antrenman planı kaydı — egzersiz başına, kendi baseline'ına göre doğrular ve güvensizse reddeder.
    ///
    /// Karşılaştırma, chat guardrail'inin önceden kullandığı double ifadesiyle birebir aynıdır (davranış değişmez).
    /// <c>fl(1.10) &gt; 1.10</c> olduğundan ve IEEE yuvarlaması monoton olduğundan, hesaplanan sınır gerçek sınırın altına
    /// düşmez: sınır temsil edilebilir bir sayıysa (100 → 110) tam %10 her zaman güvenli kabul edilir.
    /// </summary>
    public static class AcsmProgressionRule
    {
        public const double MaxProgressionRate = 0.10;

        public static double SafeMaxKg(double baselineKg) => baselineKg * (1 + MaxProgressionRate);

        public static bool IsWithinLimit(double recommendedKg, double baselineKg) => recommendedKg <= SafeMaxKg(baselineKg);

        /// <summary>
        /// Egzersiz adı → en güncel maksimum ağırlık (kg). Önce haftalık trendlerin en güncel haftası, yoksa son
        /// antrenmanlardaki maksimum kullanılır. Context yalnızca tek bir kullanıcıya ait olduğundan baseline da öyledir.
        /// </summary>
        public static Dictionary<string, double> BuildBaselines(FitBotContextDto context)
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var trend in context.WeightTrends)
            {
                if (string.IsNullOrWhiteSpace(trend.ExerciseName))
                    continue;

                var recentMax = trend.WeeklyMaxWeights
                    .Where(w => w.MaxKg > 0)
                    .OrderBy(w => w.WeeksAgo)
                    .FirstOrDefault();

                if (recentMax != null)
                    dict[trend.ExerciseName.Trim()] = recentMax.MaxKg;
            }

            // Fill in any exercises from recent workouts that are not in WeightTrends
            foreach (var workout in context.RecentWorkouts)
            {
                foreach (var ex in workout.Exercises)
                {
                    if (!string.IsNullOrWhiteSpace(ex.ExerciseName) && ex.MaxWeightKg > 0)
                        dict.TryAdd(ex.ExerciseName.Trim(), ex.MaxWeightKg);
                }
            }

            return dict;
        }
    }
}
