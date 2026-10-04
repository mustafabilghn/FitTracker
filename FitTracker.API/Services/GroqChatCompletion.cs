using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;

namespace FitTrackr.API.Services
{
    /// <summary>
    /// FitBot'un LLM bağlantısı: Semantic Kernel'ın OpenAI-uyumlu chat completion connector'ü Groq'a yönlendirilir.
    /// Yapılandırma eskisi gibi <c>Groq:ApiKey</c> ve <c>Groq:Model</c> anahtarlarından okunur.
    /// </summary>
    public static class GroqChatCompletion
    {
        public const string HttpClientName = "Groq";
        public const string DefaultModel = "llama-3.1-8b-instant";
        public static readonly Uri Endpoint = new("https://api.groq.com/openai/v1/");

        /// <summary>
        /// <see cref="Kernel"/> transient, chat completion servisi scoped kaydedilir. Böylece ileride request-scoped
        /// plugin'ler (EF Core / current user) singleton bir Kernel graph'ına capture edilmeden eklenebilir.
        /// Chat completion servisi yalnızca ilk kullanımda oluşturulur; Groq yapılandırması eksikse uygulama
        /// açılışta değil, eskisi gibi çağrı anında hata verir.
        /// </summary>
        public static IServiceCollection AddGroqSemanticKernel(this IServiceCollection services)
        {
            services.AddHttpClient(HttpClientName);
            services.AddKernel();
            services.AddScoped<IChatCompletionService>(CreateChatCompletionService);
            return services;
        }

        private static IChatCompletionService CreateChatCompletionService(IServiceProvider services)
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var apiKey = configuration["Groq:ApiKey"] ?? string.Empty;
            var model = configuration["Groq:Model"] ?? DefaultModel;

            var httpClient = services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new OpenAIClientOptions
            {
                Endpoint = Endpoint,
                Transport = new HttpClientPipelineTransport(httpClient),
                // OpenAI SDK'sı varsayılan olarak 429/5xx'te otomatik tekrar dener. Önceki HttpClient
                // implementasyonu hiç tekrar denemiyordu (429 kullanıcıya hemen "çok fazla istek" olarak
                // dönüyordu), bu yüzden davranışı korumak için retry kapalı.
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
            };

            var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
            return new OpenAIChatCompletionService(model, client, services.GetService<ILoggerFactory>());
        }
    }
}
