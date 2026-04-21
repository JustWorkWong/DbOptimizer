using DbOptimizer.Infrastructure.Llm;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Prompts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI;
using System.ClientModel;

namespace DbOptimizer.Infrastructure.DependencyInjection;

public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddLlmInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LlmProviderOptions>(configuration.GetSection(LlmProviderOptions.SectionName));
        services.Configure<MafFeatureFlags>(configuration.GetSection(MafFeatureFlags.SectionName));

        services.AddChatClient(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<LlmProviderOptions>>().Value;
            return CreateChatClient(options);
        });

        services.AddSingleton<IChatClientService, ChatClientService>();
        services.AddSingleton<ILlmPromptManager, LlmPromptManager>();
        services.AddSingleton<ILlmExecutionLogger, LlmExecutionLogger>();

        return services;
    }

    private static IChatClient CreateChatClient(LlmProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new InvalidOperationException("LlmProvider:Model is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("LlmProvider:ApiKey is required.");
        }

        var provider = options.Provider.Trim();
        if (!string.Equals(provider, "DashScope", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported LLM provider '{options.Provider}'.");
        }

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            clientOptions.Endpoint = new Uri(options.Endpoint, UriKind.Absolute);
        }

        var chatClient = new ChatClient(
            options.Model,
            new ApiKeyCredential(options.ApiKey),
            clientOptions);

        return chatClient.AsIChatClient();
    }
}
