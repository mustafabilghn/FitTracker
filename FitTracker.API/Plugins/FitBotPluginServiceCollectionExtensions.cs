using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel;

namespace FitTrackr.API.Plugins
{
    public static class FitBotPluginServiceCollectionExtensions
    {
        /// <summary>
        /// <see cref="WorkoutPlugin"/>'i Kernel'e request-scoped olarak ekler.
        ///
        /// Dikkat: Semantic Kernel'in <c>AddFromType&lt;T&gt;()</c> kısayoluna bilerek güvenilmiyor; plugin'i bizim
        /// scoped kayıtlarımızdan çözümleyip <see cref="KernelPlugin"/>'i TRANSIENT kaydediyoruz. Kernel de transient
        /// olduğundan her Kernel, o request scope'unun plugin/current-user örneğini alır; scoped bağımlılıklar
        /// (EF Core DbContext, current user) singleton bir object graph'a capture edilmez. Kernel root provider'dan
        /// çözülmeye çalışılırsa (scope doğrulaması açıkken) hata verir, sessizce yanlış scope'a bağlanmaz.
        /// </summary>
        public static IServiceCollection AddFitBotWorkoutPlugin(this IServiceCollection services)
        {
            services.AddKernel();
            services.AddScoped<ICurrentUserContext, CurrentUserContext>();
            services.AddScoped<WorkoutPlugin>();
            services.AddTransient<KernelPlugin>(sp =>
                KernelPluginFactory.CreateFromObject(sp.GetRequiredService<WorkoutPlugin>(), WorkoutPlugin.PluginName));

            // Durumsuz (stateless) filter: tool turu sayısını sınırlar (token/rate-limit koruması).
            services.AddSingleton<IAutoFunctionInvocationFilter, ToolRoundLimitFilter>();

            return services;
        }

        /// <summary>
        /// FitBot'un write aksiyonu <see cref="WorkoutPlanPlugin"/>'i (SaveWorkoutPlan) okuma plugin'iyle aynı desenle,
        /// request-scoped olarak ekler. Bağımlılıklar: <see cref="ICurrentUserContext"/>, <see cref="IWorkoutAnalysisService"/>,
        /// IWorkoutRepository ve mevcut FluentValidation validator'ları (Program.cs'te kayıtlı).
        /// Tool'lar, okuma tool'larıyla aynı şekilde yalnızca serbest sohbette sunulur (AiWorkoutCoachService gating).
        /// </summary>
        public static IServiceCollection AddFitBotWorkoutPlanPlugin(this IServiceCollection services)
        {
            services.AddKernel();
            services.TryAddScoped<ICurrentUserContext, CurrentUserContext>();
            services.AddScoped<WorkoutPlanValidator>();
            services.AddScoped<WorkoutPlanSaveOutcome>(); // request başına authoritative write sonucu
            services.AddScoped<WorkoutPlanPlugin>(); // scoped: mükerrer kayıt koruması request başınadır
            services.AddTransient<KernelPlugin>(sp =>
                KernelPluginFactory.CreateFromObject(sp.GetRequiredService<WorkoutPlanPlugin>(), WorkoutPlanPlugin.PluginName));

            // Write sonrası LLM'e geri dönülmez: döngü SaveWorkoutPlan'dan sonra biter, cevabı sunucu üretir.
            services.AddSingleton<IAutoFunctionInvocationFilter, WriteActionTerminationFilter>();

            return services;
        }
    }
}
