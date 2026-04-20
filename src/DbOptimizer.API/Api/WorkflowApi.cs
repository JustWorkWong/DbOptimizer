using System.Collections.Concurrent;
using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.Workflows.Application;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.API.Api;

internal static class WorkflowApiRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapWorkflowApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workflows");

        group.MapPost("/sql-analysis", HandleCreateSqlAnalysisAsync);
        group.MapPost("/db-config-optimization", HandleCreateDbConfigOptimizationAsync);
        group.MapGet("/{sessionId:guid}", HandleGetWorkflowAsync);
        group.MapPost("/{sessionId:guid}/cancel", HandleCancelWorkflowAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateSqlAnalysisAsync(
        CreateSqlAnalysisWorkflowRequest request,
        IWorkflowApplicationService workflowApplicationService,
        IValidator<CreateSqlAnalysisWorkflowRequest> validator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return await validator.ValidateAndExecuteAsync(request, httpContext, async () =>
        {
            try
            {
                var response = await workflowApplicationService.StartSqlAnalysisAsync(request, cancellationToken);
                return ApiEnvelopeFactory.Success(httpContext, response);
            }
            catch (WorkflowExecutionThrottledException ex)
            {
                return ApiEnvelopeFactory.Failure(
                    httpContext,
                    StatusCodes.Status429TooManyRequests,
                    "WORKFLOW_CONCURRENCY_LIMIT_REACHED",
                    ex.Message,
                    new
                    {
                        ex.WorkflowType,
                        ex.TotalLimit,
                        ex.WorkflowTypeLimit,
                        ex.TotalActiveRuns,
                        ex.WorkflowTypeActiveRuns
                    });
            }
            catch (InvalidOperationException ex)
            {
                return ApiEnvelopeFactory.Failure(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", ex.Message, null);
            }
            catch (ApiException ex)
            {
                return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
            }
        });
    }

    private static async Task<IResult> HandleCreateDbConfigOptimizationAsync(
        CreateDbConfigOptimizationWorkflowRequest request,
        IWorkflowApplicationService workflowApplicationService,
        IValidator<CreateDbConfigOptimizationWorkflowRequest> validator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return await validator.ValidateAndExecuteAsync(request, httpContext, async () =>
        {
            try
            {
                var response = await workflowApplicationService.StartDbConfigOptimizationAsync(request, cancellationToken);
                return ApiEnvelopeFactory.Success(httpContext, response);
            }
            catch (WorkflowExecutionThrottledException ex)
            {
                return ApiEnvelopeFactory.Failure(
                    httpContext,
                    StatusCodes.Status429TooManyRequests,
                    "WORKFLOW_CONCURRENCY_LIMIT_REACHED",
                    ex.Message,
                    new
                    {
                        ex.WorkflowType,
                        ex.TotalLimit,
                        ex.WorkflowTypeLimit,
                        ex.TotalActiveRuns,
                        ex.WorkflowTypeActiveRuns
                    });
            }
            catch (InvalidOperationException ex)
            {
                return ApiEnvelopeFactory.Failure(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", ex.Message, null);
            }
            catch (ApiException ex)
            {
                return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
            }
        });
    }

    private static async Task<IResult> HandleGetWorkflowAsync(
        Guid sessionId,
        IWorkflowApplicationService workflowApplicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var response = await workflowApplicationService.GetAsync(sessionId, cancellationToken);
        if (response is null)
        {
            return ApiEnvelopeFactory.Failure(
                httpContext,
                StatusCodes.Status404NotFound,
                "WORKFLOW_NOT_FOUND",
                "Workflow session not found.",
                new { sessionId });
        }

        return ApiEnvelopeFactory.Success(httpContext, response);
    }

    private static async Task<IResult> HandleCancelWorkflowAsync(
        Guid sessionId,
        IWorkflowApplicationService workflowApplicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workflowApplicationService.CancelAsync(sessionId, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (InvalidOperationException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, StatusCodes.Status400BadRequest, "INVALID_OPERATION", ex.Message, null);
        }
        catch (ApiException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
    }

}
