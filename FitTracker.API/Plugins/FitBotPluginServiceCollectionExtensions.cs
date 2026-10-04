using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
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
    }
}
