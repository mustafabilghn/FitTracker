using System;
using CommunityToolkit.VectorData.Qdrant;
using FitTrackr.API.RAG;
using FitTrackr.API.Services;
using FitTrackr.API.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Qdrant.Client;

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

        /// <summary>
        /// Genel fitness bilgi tabanı aramasını (<see cref="KnowledgePlugin"/>, SearchFitnessKnowledge) ekler.
        /// <c>Rag:Enabled</c> false ise HİÇBİR şey kaydedilmez: tool listesi ve system prompt NON-RAG ile aynı kalır.
        ///
        /// Vector store (Qdrant) ve embedding (Ollama) servisleri tembel oluşturulur; açılışta ağ çağrısı yapılmaz, bu yüzden
        /// backend'ler kapalıyken uygulama ve Chat endpoint'i normal açılır. Servisler TryAdd ile kaydedildiğinden testler
        /// kendi in-memory store / sahte embedding implementasyonlarını önceden kaydedebilir.
        /// </summary>
        public static IServiceCollection AddFitBotKnowledgePlugin(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection(RagOptions.SectionName);
            services.Configure<RagOptions>(section);
            var options = section.Get<RagOptions>() ?? new RagOptions();
            if (!options.Enabled)
                return services;

            services.AddKernel();
            services.AddHttpClient(OllamaFitnessEmbeddingService.HttpClientName, (sp, client) =>
            {
                var rag = sp.GetRequiredService<IOptions<RagOptions>>().Value;
                client.BaseAddress = new Uri(rag.EmbeddingEndpoint.TrimEnd('/') + "/");
                // Zaman aşımı çağrı başına uygulanır (sorgu: Rag:TimeoutSeconds, ingestion: Rag:IngestionTimeoutSeconds).
                client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            });

            services.TryAddSingleton<IFitnessEmbeddingService, OllamaFitnessEmbeddingService>();
            services.TryAddKeyedSingleton<VectorStore>(RagServiceKeys.VectorStore, (sp, _) =>
                CreateQdrantVectorStore(sp.GetRequiredService<IOptions<RagOptions>>().Value));
            services.TryAddSingleton<IFitnessKnowledgeCorpusSource, FitnessKnowledgeCorpusSource>();
            services.TryAddSingleton<KnowledgeAvailability>();
            services.TryAddSingleton<FitnessKnowledgeCollectionProvider>();
            services.TryAddSingleton<IKnowledgeIngestionService, KnowledgeIngestionService>();
            services.TryAddSingleton<IFitnessKnowledgeSearchService, FitnessKnowledgeSearchService>();

            // Kullanıcıya bağlı durum taşımaz (yalnızca bilgi tabanı): singleton plugin, diğerleriyle aynı transient KernelPlugin deseni.
            services.TryAddSingleton<KnowledgePlugin>();
            services.AddTransient<KernelPlugin>(sp =>
                KernelPluginFactory.CreateFromObject(sp.GetRequiredService<KnowledgePlugin>(), KnowledgePlugin.PluginName));

            services.AddHostedService<KnowledgeIngestionHostedService>();
            return services;
        }

        // QdrantClient gRPC kullanır; host REST endpoint'inden (Rag:QdrantEndpoint) alınır, port Rag:QdrantGrpcPort'tur.
        // Bağlantı ilk çağrıda kurulur. API key yok (lokal Docker).
        private static VectorStore CreateQdrantVectorStore(RagOptions options)
        {
            var endpoint = new Uri(options.QdrantEndpoint);
            var client = new QdrantClient(
                endpoint.Host,
                options.QdrantGrpcPort,
                https: endpoint.Scheme == Uri.UriSchemeHttps,
                grpcTimeout: TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));
            return new QdrantVectorStore(client, ownsClient: true);
        }
    }
}
