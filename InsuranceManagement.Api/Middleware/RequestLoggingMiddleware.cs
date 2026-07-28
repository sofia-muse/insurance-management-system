using System.Diagnostics;

namespace InsuranceManagement.Api.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.StartNew();
        _logger.LogInformation(
            "RequestStart method={Method} path={Path} traceId={TraceId}",
            context.Request.Method,
            context.Request.Path,
            context.TraceIdentifier);

        try
        {
            await _next(context);
        }
        finally
        {
            startedAt.Stop();
            _logger.LogInformation(
                "RequestEnd method={Method} path={Path} statusCode={StatusCode} durationMs={DurationMs} traceId={TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                startedAt.ElapsedMilliseconds,
                context.TraceIdentifier);
        }
    }
}
