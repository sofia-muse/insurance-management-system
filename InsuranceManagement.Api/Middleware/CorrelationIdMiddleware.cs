using System.Diagnostics;
using System.Text.RegularExpressions;

namespace InsuranceManagement.Api.Middleware;

public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 64;
    private static readonly Regex AllowedChars = new("^[A-Za-z0-9\\-_.]+$", RegexOptions.Compiled);
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing)
            && !string.IsNullOrWhiteSpace(existing) &&
            IsValid(existing.ToString())
                ? existing.ToString().Trim()
                : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await _next(context);
    }

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        return normalized.Length <= MaxLength && AllowedChars.IsMatch(normalized);
    }
}
