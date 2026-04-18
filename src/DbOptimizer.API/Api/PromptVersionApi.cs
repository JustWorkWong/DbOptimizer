using DbOptimizer.Infrastructure.Prompts;

namespace DbOptimizer.API.Api;

internal static class PromptVersionApiRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapPromptVersionApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/prompt-versions");

        group.MapGet(string.Empty, HandleListPromptVersionsAsync);
        group.MapPost(string.Empty, HandleCreatePromptVersionAsync);
        group.MapPost("/{versionId:guid}/activate", HandleActivatePromptVersionAsync);
        group.MapPost("/rollback", HandleRollbackPromptVersionAsync);

        // RESTful 风格路由
        var restGroup = endpoints.MapGroup("/api/prompts/{agentName}/versions");
        restGroup.MapGet(string.Empty, HandleListVersionsByAgentAsync);
        restGroup.MapGet("/active", HandleGetActiveVersionAsync);
        restGroup.MapGet("/{versionNumber:int}", HandleGetVersionByNumberAsync);
        restGroup.MapPost(string.Empty, HandleCreateVersionForAgentAsync);
        restGroup.MapPut("/{versionNumber:int}/activate", HandleActivateVersionByNumberAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleListPromptVersionsAsync(
        string? agentName,
        int? page,
        int? pageSize,
        IPromptVersionService promptVersionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await promptVersionService.ListAsync(
                agentName,
                page ?? 1,
                pageSize ?? 20,
                cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (Exception ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 500, "INTERNAL_ERROR", ex.Message);
        }
    }

    private static async Task<IResult> HandleCreatePromptVersionAsync(
        CreatePromptVersionRequest request,
        IPromptVersionService promptVersionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await promptVersionService.CreateAsync(request, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (Exception ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 500, "INTERNAL_ERROR", ex.Message);
        }
    }

    private static async Task<IResult> HandleActivatePromptVersionAsync(
        Guid versionId,
        IPromptVersionService promptVersionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await promptVersionService.ActivateAsync(versionId, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, new { VersionId = versionId, Message = "Activated" });
        }
        catch (InvalidOperationException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 404, "NOT_FOUND", ex.Message);
        }
        catch (Exception ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 500, "INTERNAL_ERROR", ex.Message);
        }
    }

    private static async Task<IResult> HandleRollbackPromptVersionAsync(
        RollbackPromptVersionRequest request,
        IPromptVersionService promptVersionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await promptVersionService.RollbackAsync(
                request.AgentName,
                request.VersionNumber,
                cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, new
            {
                request.AgentName,
                request.VersionNumber,
                Message = "Rolled back"
            });
        }
        catch (InvalidOperationException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 404, "NOT_FOUND", ex.Message);
        }
        catch (Exception ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 500, "INTERNAL_ERROR", ex.Message);
        }
    }

    private static async Task<IResult> HandleListVersionsByAgentAsync(
        string agentName,
        int? page,
        int? pageSize,
        IPromptVersionService promptVersionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await promptVersionService.ListAsync(
                agentName,
                page ?? 1,
                pageSize ?? 20,
                cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (Exception ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 500, "INTERNAL_ERROR", ex.Message);
        }
    }

    private static async Task<IResult> HandleGetActiveVersionAsync(
        string agentName,
        IPromptVersionService promptVersionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await promptVersionService.GetActiveAsync(agentName, cancellationToken);
            if (response == null)
            {
                return ApiEnvelopeFactory.Failure(
                    httpContext,
                    404,
                    "NOT_FOUND",
                    $"No active version found for agent: {agentName}");
            }
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (Exception ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 500, "INTERNAL_ERROR", ex.Message);
        }
    }

    private static async Task<IResult> HandleGetVersionByNumberAsync(
        string agentName,
        int versionNumber,
        IPromptVersionService promptVersionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await promptVersionService.GetByVersionAsync(
                agentName,
                versionNumber,
                cancellationToken);
            if (response == null)
            {
                return ApiEnvelopeFactory.Failure(
                    httpContext,
                    404,
                    "NOT_FOUND",
                    $"Version {versionNumber} not found for agent: {agentName}");
            }
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (Exception ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 500, "INTERNAL_ERROR", ex.Message);
        }
    }

    private static async Task<IResult> HandleCreateVersionForAgentAsync(
        string agentName,
        CreateVersionForAgentRequest request,
        IPromptVersionService promptVersionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var createRequest = new CreatePromptVersionRequest(
                agentName,
                request.PromptTemplate,
                request.Variables,
                request.CreatedBy);

            var response = await promptVersionService.CreateAsync(createRequest, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (Exception ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 500, "INTERNAL_ERROR", ex.Message);
        }
    }

    private static async Task<IResult> HandleActivateVersionByNumberAsync(
        string agentName,
        int versionNumber,
        IPromptVersionService promptVersionService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await promptVersionService.RollbackAsync(agentName, versionNumber, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, new
            {
                AgentName = agentName,
                VersionNumber = versionNumber,
                Message = "Activated"
            });
        }
        catch (InvalidOperationException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 404, "NOT_FOUND", ex.Message);
        }
        catch (Exception ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, 500, "INTERNAL_ERROR", ex.Message);
        }
    }
}

internal sealed record RollbackPromptVersionRequest(string AgentName, int VersionNumber);

internal sealed record CreateVersionForAgentRequest(
    string PromptTemplate,
    string? Variables = null,
    string? CreatedBy = null);
