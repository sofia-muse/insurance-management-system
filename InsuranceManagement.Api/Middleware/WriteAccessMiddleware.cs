using InsuranceManagement.Api.Utilities;
using System.Security.Claims;

namespace InsuranceManagement.Api.Middleware;

public class WriteAccessMiddleware
{
    public const string HeaderName = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public WriteAccessMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!RequiresWriteAccessCheck(context.Request))
        {
            await _next(context);
            return;
        }

        var apiKeyEnabled = _configuration.GetValue<bool>("ApiSecurity:EnableLegacyWriteApiKey");
        if (!apiKeyEnabled)
        {
            await _next(context);
            return;
        }

        if (context.Request.Headers.TryGetValue("Authorization", out var authorizationHeader)
            && authorizationHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var configuredKey = _configuration["ApiSecurity:WriteApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "WRITE_ACCESS_CONFIG_INVALID",
                $"Configuration key 'ApiSecurity:WriteApiKey' is required when legacy API-key write access is enabled.");
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var providedKey) || string.IsNullOrWhiteSpace(providedKey))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "WRITE_ACCESS_REQUIRED",
                $"Missing required header '{HeaderName}'.");
            return;
        }

        if (!string.Equals(providedKey.ToString(), configuredKey, StringComparison.Ordinal))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "WRITE_ACCESS_FORBIDDEN",
                "Invalid API key.");
            return;
        }

        context.Items["LegacyWriteAccessGranted"] = true;
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "legacy-api-key"),
                    new Claim(ClaimTypes.Role, "writer"),
                    new Claim("scope", "api.write")
                ],
                authenticationType: "LegacyApiKey"));
        }

        await _next(context);
    }

    private static bool RequiresWriteAccessCheck(HttpRequest request)
    {
        return HttpMethods.IsPost(request.Method)
            || HttpMethods.IsPut(request.Method)
            || HttpMethods.IsPatch(request.Method)
            || HttpMethods.IsDelete(request.Method);
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string code, string detail)
    {
        var title = statusCode switch
        {
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status403Forbidden => "Forbidden",
            _ => "Internal Server Error"
        };
        await ApiErrorResponseWriter.WriteAsync(context, statusCode, title, code, detail);
    }
}
