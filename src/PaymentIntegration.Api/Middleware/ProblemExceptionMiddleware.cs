using System.Text.Json;
using PaymentIntegration.Application.Common;

namespace PaymentIntegration.Api.Middleware;

/// <summary>
/// Turns an AppException into RFC 7807 problem details. Anything else is logged
/// and returned as a generic 500 so internal details stay off the wire.
/// </summary>
public sealed class ProblemExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ProblemExceptionMiddleware(
        RequestDelegate next,
        ILogger<ProblemExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (AppException exception)
        {
            await WriteAsync(context, exception.StatusCode, exception.Title, exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
            var detail = _environment.IsDevelopment()
                ? exception.Message
                : "An unexpected error occurred.";
            await WriteAsync(context, StatusCodes.Status500InternalServerError, "Unexpected error", detail);
        }
    }

    private static async Task WriteAsync(HttpContext context, int status, string title, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type = "about:blank",
            title,
            status,
            detail,
            instance = context.Request.Path.Value,
            traceId = context.TraceIdentifier
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
