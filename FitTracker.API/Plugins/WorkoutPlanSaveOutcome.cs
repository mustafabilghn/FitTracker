namespace FitTrackr.API.Plugins
{
    /// <summary>
    /// Bir request scope'unda SaveWorkoutPlan'ın sunucu tarafındaki (authoritative) sonucu. Plugin yazar,
    /// AiWorkoutCoachService okur ve kullanıcı cevabını LLM'e yazdırmadan, bu sonuçtan deterministik olarak üretir.
    /// DI: scoped.
    /// </summary>
    public sealed class WorkoutPlanSaveOutcome
    {
        public SaveWorkoutPlanResult? Result { get; private set; }
        public int ExerciseCount { get; private set; }
        public int SetCount { get; private set; }

        /// <summary>Guardrail reddinde, ihlal eden en yüksek önerilen ağırlık.</summary>
        public double? RejectedWeightKg { get; private set; }

        public bool HasResult => Result is not null;

        internal void Record(SaveWorkoutPlanResult result, int exerciseCount, int setCount, double? rejectedWeightKg)
        {
            Result = result;
            ExerciseCount = exerciseCount;
            SetCount = setCount;
            RejectedWeightKg = rejectedWeightKg;
        }
    }
}
