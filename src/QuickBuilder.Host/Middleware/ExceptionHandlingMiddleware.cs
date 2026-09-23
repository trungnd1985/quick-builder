using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Infrastructure.Dispatch;

namespace QuickBuilder.Host.Middleware;

/// <summary>
/// Turns unhandled exceptions into problem details that reveal nothing internal (FR-034).
/// </summary>
/// <remarks>
/// Two cases, deliberately distinguishable (FR-008):
/// <list type="bullet">
/// <item><see cref="RequestValidationException"/> — thrown by the dispatcher before any
/// handler ran. Becomes a 400 naming the invalid inputs.</item>
/// <item>Anything else — unexpected. Logged with the trace id, returned as a 500 carrying
/// only that trace id, so an operator can find the log entry without the caller learning
/// anything about the internals.</item>
/// </list>
/// </remarks>
internal sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (RequestValidationException ex)
        {
            await WriteValidationProblemAsync(context, ex).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // FR-031a: the trace id in the log is the same value returned to the caller,
            // so "it failed, here's the id" is enough to find the entry.
            var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

            logger.LogError(ex, "Unhandled exception while handling {Method} {Path}.",
                context.Request.Method, context.Request.Path);

            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "https://quickbuilder.dev/errors/internal",
                "An unexpected error occurred",
                "internal.unexpected",
                detail: null,
                traceId).ConfigureAwait(false);
        }
    }

    private static async Task WriteValidationProblemAsync(HttpContext context, RequestValidationException ex)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        var problem = new ValidationProblemDetails(ex.Result.ToDictionary())
        {
            Type = "https://quickbuilder.dev/errors/validation",
            Title = "The request failed validation",
            Status = StatusCodes.Status400BadRequest,
        };
        problem.Extensions["errorCode"] = "request.invalid";
        problem.Extensions["traceId"] = traceId;

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string type,
        string title,
        string errorCode,
        string? detail,
        string traceId)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = status,
            Detail = detail,
        };
        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = traceId;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted).ConfigureAwait(false);
    }
}
