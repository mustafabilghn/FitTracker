using System;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace FitTrackr.API.Plugins
{
    /// <summary>
    /// Otomatik function calling'in tool turu sayısını sınırlar.
    ///
    /// Neden: Semantic Kernel varsayılan olarak otomatik tool turlarını sınırlamaz; model sürekli tool çağırırsa her
    /// turda tüm prompt yeniden gönderilir ve Groq free plan limitleri (8K TPM / 200K TPD) hızla tükenir.
    /// Son izin verilen turdaki fonksiyonlar çalıştırıldıktan sonra döngü <see cref="AutoFunctionInvocationContext.Terminate"/>
    /// ile sonlandırılır; <c>AiWorkoutCoachService</c> bu durumda tool çağrısına izin vermeyen tek bir son istekle
    /// cevabı alır. Yalnızca SK'nın public filter mekanizması kullanılır.
    /// </summary>
    public sealed class ToolRoundLimitFilter : IAutoFunctionInvocationFilter
    {
        public const int MaxToolRounds = 3;

        public async Task OnAutoFunctionInvocationAsync(
            AutoFunctionInvocationContext context,
            Func<AutoFunctionInvocationContext, Task> next)
        {
            await next(context);

            // RequestSequenceIndex 0'dan başlar. Son izin verilen turun son fonksiyonundan sonra döngüyü bitir.
            var isLastAllowedRound = context.RequestSequenceIndex >= MaxToolRounds - 1;
            var isLastFunctionOfRound = context.FunctionSequenceIndex >= context.FunctionCount - 1;
            if (isLastAllowedRound && isLastFunctionOfRound)
                context.Terminate = true;
        }
    }
}
