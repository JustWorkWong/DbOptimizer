using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DbOptimizer.Infrastructure.Persistence;

namespace DbOptimizer.Infrastructure.Prompts;

/* =========================
 * Prompt 版本管理服务
 * 职责：
 * 1) 创建新版本（自动递增版本号）
 * 2) 激活指定版本（同 agent 其他版本自动失活）
 * 3) 回滚到历史版本
 * 4) 分页查询版本列表
 * ========================= */
public sealed class PromptVersionService(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    ILogger<PromptVersionService> logger) : IPromptVersionService
{
    public async Task<PromptVersionListResponse> ListAsync(
        string? agentName = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.PromptVersions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(agentName))
        {
            query = query.Where(x => x.AgentName == agentName);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new PromptVersionDto(
                x.VersionId,
                x.AgentName,
                x.VersionNumber,
                x.PromptTemplate,
                x.Variables,
                x.IsActive,
                x.CreatedAt,
                x.CreatedBy))
            .ToListAsync(cancellationToken);

        return new PromptVersionListResponse(items, total, page, pageSize);
    }

    public async Task<PromptVersionDto?> GetActiveAsync(
        string agentName,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await dbContext.PromptVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.AgentName == agentName && x.IsActive,
                cancellationToken);

        return entity == null ? null : new PromptVersionDto(
            entity.VersionId,
            entity.AgentName,
            entity.VersionNumber,
            entity.PromptTemplate,
            entity.Variables,
            entity.IsActive,
            entity.CreatedAt,
            entity.CreatedBy);
    }

    public async Task<PromptVersionDto?> GetByVersionAsync(
        string agentName,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await dbContext.PromptVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.AgentName == agentName && x.VersionNumber == versionNumber,
                cancellationToken);

        return entity == null ? null : new PromptVersionDto(
            entity.VersionId,
            entity.AgentName,
            entity.VersionNumber,
            entity.PromptTemplate,
            entity.Variables,
            entity.IsActive,
            entity.CreatedAt,
            entity.CreatedBy);
    }

    public async Task<PromptVersionDto> CreateAsync(
        CreatePromptVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var maxVersion = await dbContext.PromptVersions
            .Where(x => x.AgentName == request.AgentName)
            .MaxAsync(x => (int?)x.VersionNumber, cancellationToken) ?? 0;

        var entity = new PromptVersionEntity
        {
            VersionId = Guid.NewGuid(),
            AgentName = request.AgentName,
            VersionNumber = maxVersion + 1,
            PromptTemplate = request.PromptTemplate,
            Variables = request.Variables,
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = request.CreatedBy
        };

        dbContext.PromptVersions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Prompt version created. AgentName={AgentName}, VersionNumber={VersionNumber}, VersionId={VersionId}",
            entity.AgentName,
            entity.VersionNumber,
            entity.VersionId);

        return new PromptVersionDto(
            entity.VersionId,
            entity.AgentName,
            entity.VersionNumber,
            entity.PromptTemplate,
            entity.Variables,
            entity.IsActive,
            entity.CreatedAt,
            entity.CreatedBy);
    }

    public async Task ActivateAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var target = await dbContext.PromptVersions
            .FirstOrDefaultAsync(x => x.VersionId == versionId, cancellationToken);

        if (target == null)
        {
            throw new InvalidOperationException($"Prompt version {versionId} not found.");
        }

        await DeactivateActiveVersionsAsync(dbContext, target.AgentName, cancellationToken);

        target.IsActive = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Prompt version activated. AgentName={AgentName}, VersionNumber={VersionNumber}, VersionId={VersionId}",
            target.AgentName,
            target.VersionNumber,
            target.VersionId);
    }

    public async Task RollbackAsync(
        string agentName,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var target = await dbContext.PromptVersions
            .FirstOrDefaultAsync(
                x => x.AgentName == agentName && x.VersionNumber == versionNumber,
                cancellationToken);

        if (target == null)
        {
            throw new InvalidOperationException(
                $"Prompt version not found. AgentName={agentName}, VersionNumber={versionNumber}");
        }

        await DeactivateActiveVersionsAsync(dbContext, agentName, cancellationToken);

        target.IsActive = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Prompt version rolled back. AgentName={AgentName}, VersionNumber={VersionNumber}, VersionId={VersionId}",
            target.AgentName,
            target.VersionNumber,
            target.VersionId);
    }

    private static async Task DeactivateActiveVersionsAsync(
        DbOptimizerDbContext dbContext,
        string agentName,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal))
        {
            var activeVersions = await dbContext.PromptVersions
                .Where(x => x.AgentName == agentName && x.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var activeVersion in activeVersions)
            {
                activeVersion.IsActive = false;
            }

            return;
        }

        await dbContext.PromptVersions
            .Where(x => x.AgentName == agentName && x.IsActive)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.IsActive, false),
                cancellationToken);
    }
}
