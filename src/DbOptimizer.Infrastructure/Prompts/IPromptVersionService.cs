namespace DbOptimizer.Infrastructure.Prompts;

public sealed record CreatePromptVersionRequest(
    string AgentName,
    string PromptTemplate,
    string? Variables = null,
    string? CreatedBy = null);

public sealed record PromptVersionDto(
    Guid VersionId,
    string AgentName,
    int VersionNumber,
    string PromptTemplate,
    string? Variables,
    bool IsActive,
    DateTimeOffset CreatedAt,
    string? CreatedBy);

public sealed record PromptVersionListResponse(
    List<PromptVersionDto> Items,
    int Total,
    int Page,
    int PageSize);

public interface IPromptVersionService
{
    Task<PromptVersionListResponse> ListAsync(
        string? agentName = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<PromptVersionDto> CreateAsync(
        CreatePromptVersionRequest request,
        CancellationToken cancellationToken = default);

    Task ActivateAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task RollbackAsync(
        string agentName,
        int versionNumber,
        CancellationToken cancellationToken = default);
}
