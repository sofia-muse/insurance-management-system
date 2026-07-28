using System.Net;
using System.Text.Json;
using InsuranceManagement.Core.Exceptions;

namespace InsuranceManagement.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, code, detail) = exception switch
        {
            NotFoundException ex => (HttpStatusCode.NotFound, "Not Found", ex.Code, ex.Message),
            ConflictException ex => (HttpStatusCode.Conflict, "Conflict", ex.Code, ex.Message),
            BusinessRuleException ex => (HttpStatusCode.BadRequest, "Business Rule Violation", ex.Code, ex.Message),
            ApiException ex => (HttpStatusCode.BadRequest, "Request Error", ex.Code, ex.Message),
            _ => (HttpStatusCode.InternalServerError, "Internal Server Error", "UNEXPECTED_ERROR", "An unexpected error occurred.")
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception traceId={TraceId}", context.TraceIdentifier);
        }
        else
        {
            _logger.LogWarning(exception, "Handled exception code={Code} traceId={TraceId}", code, context.TraceIdentifier);
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response already started; skipping standardized exception payload traceId={TraceId}", context.TraceIdentifier);
            return;
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var payload = new
        {
            title,
            status = (int)statusCode,
            code,
            detail,
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
