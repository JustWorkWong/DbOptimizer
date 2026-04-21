namespace DbOptimizer.Infrastructure.Prompts;

public interface ILlmPromptManager
{
    Task<PromptVersionDto> GetActivePromptAsync(
        string agentName,
        CancellationToken cancellationToken = default);

    Task<string> GetPromptAsync(
        string agentName,
        CancellationToken cancellationToken = default);
}

public sealed class LlmPromptManager(IPromptVersionService promptVersionService) : ILlmPromptManager
{
    private readonly IPromptVersionService _promptVersionService = promptVersionService;

    public async Task<PromptVersionDto> GetActivePromptAsync(
        string agentName,
        CancellationToken cancellationToken = default)
    {
        var prompt = await _promptVersionService.GetActiveAsync(agentName, cancellationToken);
        if (prompt is null)
        {
            throw new InvalidOperationException($"No active prompt found for agent '{agentName}'.");
        }

        return prompt;
    }

    public async Task<string> GetPromptAsync(
        string agentName,
        CancellationToken cancellationToken = default)
    {
        var prompt = await GetActivePromptAsync(agentName, cancellationToken);
        return prompt.PromptTemplate;
    }
}
