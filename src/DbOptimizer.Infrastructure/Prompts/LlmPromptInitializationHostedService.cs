using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Prompts;

public sealed class LlmPromptInitializationHostedService(
    IPromptVersionService promptVersionService,
    ILogger<LlmPromptInitializationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var definition in LlmPromptSeedCatalog.Definitions)
        {
            var activePrompt = await promptVersionService.GetActiveAsync(definition.AgentName, cancellationToken);
            if (activePrompt is not null)
            {
                continue;
            }

            var createdPrompt = await promptVersionService.CreateAsync(
                new CreatePromptVersionRequest(
                    definition.AgentName,
                    definition.PromptTemplate,
                    definition.Variables,
                    definition.CreatedBy),
                cancellationToken);

            await promptVersionService.ActivateAsync(createdPrompt.VersionId, cancellationToken);

            logger.LogInformation(
                "Seeded default LLM prompt. AgentName={AgentName}, VersionId={VersionId}",
                definition.AgentName,
                createdPrompt.VersionId);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
