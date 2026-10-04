using System;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace FitTrackr.API.Plugins
{
    /// <summary>
    /// Write aksiyonu (SaveWorkoutPlan) çalıştıktan sonra otomatik function-calling döngüsünü sonlandırır.
    ///
    /// Neden: Tool sonucunu tekrar LLM'e gönderip final cevabı ona yazdırmak (1) başarılı bir kayıttan sonra ikinci bir
    /// completion ve Groq free-plan 429 riski yaratır, (2) modelin başarısız bir kaydı "kaydedildi" diye raporlamasına
    /// izin verir. Döngü burada biter; kullanıcı cevabını sunucu <see cref="WorkoutPlanSaveOutcome"/>'dan üretir.
    /// Okuma tool'larının (Workout plugin) akışı etkilenmez. Durumsuz; singleton kaydedilir.
    /// </summary>
    public sealed class WriteActionTerminationFilter : IAutoFunctionInvocationFilter
    {
        public async Task OnAutoFunctionInvocationAsync(
            AutoFunctionInvocationContext context,
            Func<AutoFunctionInvocationContext, Task> next)
        {
            await next(context);

            if (context.Function.PluginName == WorkoutPlanPlugin.PluginName
                && context.Function.Name == WorkoutPlanPlugin.SaveFunctionName)
            {
                context.Terminate = true;
            }
        }
    }
}
